using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProjectCal.Api.Data;
using ProjectCal.Shared;

namespace ProjectCal.Worker;

public sealed class TranscriptionWorker(
    IServiceScopeFactory scopeFactory,
    ISpeechToTextService speechToText,
    ILogger<TranscriptionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transcription worker loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessOneAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidates = await db.Transcripts
            .Include(x => x.Attachment)
            .Where(x => x.Status == TranscriptStatus.Pending || (x.Status == TranscriptStatus.Failed && x.Attempts < 3))
            .ToArrayAsync(cancellationToken);
        var transcript = candidates.OrderBy(x => x.UpdatedAt).FirstOrDefault();

        if (transcript?.Attachment is null)
        {
            return;
        }

        transcript.Status = TranscriptStatus.Processing;
        transcript.ErrorMessage = null;
        transcript.Attempts++;
        transcript.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            transcript.Text = await speechToText.TranscribeAsync(transcript.Attachment.StoredPath, transcript.Language, cancellationToken);
            transcript.Status = TranscriptStatus.Done;
            transcript.ErrorMessage = null;
        }
        catch (Exception ex)
        {
            transcript.Status = TranscriptStatus.Failed;
            transcript.ErrorMessage = ex.Message;
            logger.LogWarning(ex, "Transcription failed for attachment {AttachmentId}.", transcript.AttachmentId);
        }

        transcript.UpdatedAt = DateTimeOffset.UtcNow;

        var note = await db.Notes.FirstOrDefaultAsync(x => x.Id == transcript.NoteId, cancellationToken);
        if (note is not null)
        {
            note.UpdatedAt = transcript.UpdatedAt;
            note.SyncVersion++;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

public interface ISpeechToTextService
{
    Task<string> TranscribeAsync(string storedPath, string language, CancellationToken cancellationToken);
}

public sealed class SpeechToTextService(HttpClient httpClient, IConfiguration configuration) : ISpeechToTextService
{
    public async Task<string> TranscribeAsync(string storedPath, string language, CancellationToken cancellationToken)
    {
        var apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return await TranscribeWithOpenAIAsync(storedPath, language, apiKey, cancellationToken);
        }

        var endpoint = configuration["Transcription:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return configuration["Transcription:StubText"]
                ?? $"Audio {storedPath} queued with language '{language}'. Configure a real STT endpoint for production.";
        }

        var response = await httpClient.PostAsJsonAsync(endpoint, new { storedPath, language }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TranscriptionResponse>(cancellationToken);
        return payload?.Text ?? "";
    }

    private async Task<string> TranscribeWithOpenAIAsync(string storedPath, string language, string apiKey, CancellationToken cancellationToken)
    {
        if (!File.Exists(storedPath))
        {
            throw new FileNotFoundException("Audio file was not found.", storedPath);
        }

        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(storedPath);
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(storedPath));
        form.Add(file, "file", Path.GetFileName(storedPath));
        form.Add(new StringContent(configuration["OpenAI:TranscriptionModel"] ?? "gpt-4o-mini-transcribe"), "model");
        form.Add(new StringContent("json"), "response_format");

        if (!string.IsNullOrWhiteSpace(language) && !string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase))
        {
            form.Add(new StringContent(language), "language");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions")
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI transcription failed: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("text", out var text) ? text.GetString() ?? "" : "";
    }

    private static string GetMimeType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".mp4" => "audio/mp4",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".webm" => "audio/webm",
            ".mpeg" => "audio/mpeg",
            ".mpga" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    private sealed record TranscriptionResponse(string Text);
}
