using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ProjectCal.Shared;

namespace ProjectCal.Tests;

public sealed class AuthAndNotesTests
{
    [Fact]
    public async Task Register_and_login_returns_tokens_without_email_confirmation()
    {
        await using var factory = new ProjectCalFactory();
        var client = factory.CreateHttpsClient();

        var registered = await RegisterAsync(client, "first@example.com", "Password123!");

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("first@example.com", "Password123!"));
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();

        Assert.NotNull(auth);
        Assert.Equal(registered.UserId, auth!.User.Id);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
    }

    [Fact]
    public async Task Re_registering_existing_email_returns_conflict()
    {
        await using var factory = new ProjectCalFactory();
        var client = factory.CreateHttpsClient();

        var first = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("pending@example.com", "Password123!"));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("pending@example.com", "Password456!"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_read_each_others_notes()
    {
        await using var factory = new ProjectCalFactory();
        var first = factory.CreateHttpsClient();
        var second = factory.CreateHttpsClient();

        await RegisterAsync(first, "first@example.com", "Password123!");
        await RegisterAsync(second, "second@example.com", "Password123!");
        await LoginAsync(first, "first@example.com", "Password123!");
        await LoginAsync(second, "second@example.com", "Password123!");

        var note = new UpsertNoteRequest(null, "Private call", "Talked through launch plan", new DateOnly(2026, 6, 9), new TimeOnly(10, 0), null, 0);
        var created = await first.PostAsJsonAsync("/api/notes", note);
        created.EnsureSuccessStatusCode();

        var firstNotes = await first.GetFromJsonAsync<NoteDto[]>("/api/notes?date=2026-06-09");
        var secondNotes = await second.GetFromJsonAsync<NoteDto[]>("/api/notes?date=2026-06-09");

        Assert.Single(firstNotes!);
        Assert.Empty(secondNotes!);
    }

    [Fact]
    public async Task Sync_pushes_offline_note_and_returns_server_version()
    {
        await using var factory = new ProjectCalFactory();
        var client = factory.CreateHttpsClient();

        await RegisterAsync(client, "sync@example.com", "Password123!");
        await LoginAsync(client, "sync@example.com", "Password123!");

        var noteId = Guid.NewGuid();
        var sync = new SyncRequest(null, [
            new SyncNoteMutation(
                SyncOperation.Upsert,
                new UpsertNoteRequest(noteId, "Offline idea", "Created while offline", new DateOnly(2026, 6, 9), new TimeOnly(14, 0), new TimeOnly(15, 0), 0))
        ]);

        var response = await client.PostAsJsonAsync("/api/sync", sync);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<SyncResponse>();

        Assert.NotNull(payload);
        var syncedNote = Assert.Single(payload!.Notes);
        Assert.Equal(noteId, syncedNote.Id);
        Assert.Equal("Offline idea", syncedNote.Title);
        Assert.True(syncedNote.SyncVersion > 0);
    }

    private static async Task<RegisterResponse> RegisterAsync(HttpClient client, string email, string password)
    {
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(email, password));
        register.EnsureSuccessStatusCode();
        return (await register.Content.ReadFromJsonAsync<RegisterResponse>())!;
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        login.EnsureSuccessStatusCode();
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }
}

internal sealed class ProjectCalFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"projectcal-tests-{Guid.NewGuid():N}.db");

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["Database:EnsureCreated"] = "true",
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Jwt:Issuer"] = "ProjectCal",
                ["Jwt:Audience"] = "ProjectCal.Client",
                ["Jwt:SigningKey"] = "tests-only-change-this-signing-key-32-bytes"
            });
        });

        return base.CreateHost(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-wal", $"{_dbPath}-shm" })
        {
            TryDelete(path);
        }
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
}
