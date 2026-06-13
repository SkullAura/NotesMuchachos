using System.Diagnostics;
using System.Net;

var root = AppContext.BaseDirectory;
var logs = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ProjectCal",
    "logs");
var data = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ProjectCal",
    "data");
var media = Path.Combine(data, "media");
Directory.CreateDirectory(logs);
Directory.CreateDirectory(data);
Directory.CreateDirectory(media);

var apiExe = Path.Combine(root, "Api", "ProjectCal.Api.exe");
var workerExe = Path.Combine(root, "Worker", "ProjectCal.Worker.exe");
var clientExe = Path.Combine(root, "Client", "ProjectCal.Client.exe");

if (!File.Exists(apiExe) || !File.Exists(workerExe) || !File.Exists(clientExe))
{
    MessageBox("ProjectCal installation is incomplete. Reinstall the application.");
    return 1;
}

if (!await IsApiReadyAsync())
{
    StartHidden(apiExe, "--urls http://localhost:5009", "api");
    await WaitForApiAsync();
}

StartHidden(workerExe, "", "worker", startOnce: true);
Process.Start(new ProcessStartInfo
{
    FileName = clientExe,
    WorkingDirectory = Path.GetDirectoryName(clientExe)!,
    UseShellExecute = true
});

return 0;

void StartHidden(string fileName, string arguments, string logName, bool startOnce = false)
{
    var processName = Path.GetFileNameWithoutExtension(fileName);
    if (startOnce && Process.GetProcessesByName(processName).Length > 0)
    {
        return;
    }

    var logs = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProjectCal",
        "logs");

    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        WorkingDirectory = Path.GetDirectoryName(fileName)!,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    var databasePath = Path.Combine(data, "projectcal.db");
    startInfo.Environment["Database__Provider"] = "Sqlite";
    startInfo.Environment["ConnectionStrings__Default"] = $"Data Source={databasePath}";
    startInfo.Environment["Storage__RootPath"] = media;

    Process.Start(startInfo)?.BeginOutputReadLineSafe(
        Path.Combine(logs, $"{logName}.out.log"),
        Path.Combine(logs, $"{logName}.err.log"));
}

static async Task WaitForApiAsync()
{
    for (var attempt = 0; attempt < 20; attempt++)
    {
        if (await IsApiReadyAsync())
        {
            return;
        }

        await Task.Delay(500);
    }
}

static async Task<bool> IsApiReadyAsync()
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var response = await client.GetAsync("http://localhost:5009/api/admin/stats");
        return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized;
    }
    catch
    {
        return false;
    }
}

static void MessageBox(string text)
{
    _ = NativeMethods.MessageBox(IntPtr.Zero, text, "ProjectCal", 0x00000010);
}

internal static class ProcessExtensions
{
    public static void BeginOutputReadLineSafe(this Process process, string stdoutPath, string stderrPath)
    {
        process.OutputDataReceived += (_, e) => AppendLine(stdoutPath, e.Data);
        process.ErrorDataReceived += (_, e) => AppendLine(stderrPath, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private static void AppendLine(string path, string? line)
    {
        if (line is null)
        {
            return;
        }

        try
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
        }
    }
}

internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
