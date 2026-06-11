using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectCal.Api.Data;
using ProjectCal.Api.Data.Entities;
using ProjectCal.Shared;
using ProjectCal.Worker;

namespace ProjectCal.Tests;

public sealed class TranscriptionWorkerTests
{
    [Fact]
    public async Task Worker_processes_pending_transcript_with_sqlite()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"projectcal-worker-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        await using var provider = services.BuildServiceProvider();

        await SeedAsync(provider);

        var worker = new TranscriptionWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new StubSpeechToTextService(),
            NullLogger<TranscriptionWorker>.Instance);

        var method = typeof(TranscriptionWorker).GetMethod("ProcessOneAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(worker, [CancellationToken.None])!;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var transcript = await db.Transcripts.SingleAsync();

        Assert.Equal(TranscriptStatus.Done, transcript.Status);
        Assert.Equal("stub transcript", transcript.Text);
        Assert.Equal(1, transcript.Attempts);

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(dbPath);
    }

    private static async Task SeedAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        var user = new UserEntity
        {
            Email = "worker@example.com",
            NormalizedEmail = "WORKER@EXAMPLE.COM",
            PasswordHash = "unused",
            EmailConfirmed = true
        };
        var note = new NoteEntity
        {
            UserId = user.Id,
            Date = new DateOnly(2026, 6, 9),
            StartTime = new TimeOnly(9, 0),
            Title = "Voice note",
            Body = "Audio"
        };
        var attachment = new AttachmentEntity
        {
            UserId = user.Id,
            NoteId = note.Id,
            Type = AttachmentType.Audio,
            FileName = "voice.m4a",
            StoredPath = "voice.m4a",
            MimeType = "audio/mp4"
        };
        var transcript = new TranscriptEntity
        {
            UserId = user.Id,
            NoteId = note.Id,
            AttachmentId = attachment.Id,
            Status = TranscriptStatus.Pending,
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        db.Users.Add(user);
        db.Notes.Add(note);
        db.Attachments.Add(attachment);
        db.Transcripts.Add(transcript);
        await db.SaveChangesAsync();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class StubSpeechToTextService : ISpeechToTextService
    {
        public Task<string> TranscribeAsync(string storedPath, string language, CancellationToken cancellationToken)
        {
            return Task.FromResult("stub transcript");
        }
    }
}
