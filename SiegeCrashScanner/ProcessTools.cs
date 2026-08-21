using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace SiegeCrashScanner;

internal static class ProcessTools
{
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await outputTask, await errorTask);
    }

    public static string PowerShellArguments(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static async Task<string> RunElevatedRepairAsync(string command, IProgress<string>? progress = null)
    {
        var script = $"& {command} *>&1; exit $LASTEXITCODE";
        var result = await RunElevatedPowerShellAsync(script, progress, "Command is running. This can take several minutes…");
        return $"> {command}\n\n{result.Output.Trim()}\n\nExit code: {result.ExitCode}";
    }

    public static async Task<(int ExitCode, string Output)> RunElevatedPowerShellAsync(
        string script,
        IProgress<string>? progress = null,
        string runningMessage = "Operation is running…")
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"SiegeCrashScanner-{Guid.NewGuid():N}.txt");
        var escapedOutput = outputFile.Replace("'", "''");
        var wrappedScript = $"& {{ {script} }} *>&1 | Out-File -LiteralPath '{escapedOutput}' -Encoding utf8; exit $LASTEXITCODE";
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = PowerShellArguments(wrappedScript),
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        progress?.Report("Waiting for administrator approval…");
        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("The elevated process could not be started.");
            progress?.Report(runningMessage);
            await process.WaitForExitAsync();
            var output = File.Exists(outputFile)
                ? await File.ReadAllTextAsync(outputFile)
                : "The elevated operation finished without returning output.";
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (1223, "Administrator approval was canceled. No changes were made.");
        }
        finally
        {
            try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch { }
        }
    }
}
