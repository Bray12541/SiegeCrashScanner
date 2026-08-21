using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SiegeCrashScanner;

internal sealed record AppUpdateInfo(Version Version, string Tag, Uri ExecutableUrl, Uri ChecksumUrl);

internal static class AppUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Bray12541/SiegeCrashScanner/releases/latest";
    private const string ExecutableAssetName = "SiegeCrashScanner.exe";
    private const string ChecksumAssetName = "SiegeCrashScanner.exe.sha256";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static async Task<AppUpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var release = await client.GetFromJsonAsync<GitHubRelease>(LatestReleaseApi, cancellationToken);
        if (release is null || release.Draft || release.Prerelease) return null;

        var versionText = release.TagName.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var version) || version <= CurrentVersion) return null;

        var executable = release.Assets.FirstOrDefault(a =>
            a.Name.Equals(ExecutableAssetName, StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets.FirstOrDefault(a =>
            a.Name.Equals(ChecksumAssetName, StringComparison.OrdinalIgnoreCase));
        if (executable is null || checksum is null ||
            !Uri.TryCreate(executable.DownloadUrl, UriKind.Absolute, out var executableUrl) ||
            !Uri.TryCreate(checksum.DownloadUrl, UriKind.Absolute, out var checksumUrl)) return null;

        return new AppUpdateInfo(version, release.TagName, executableUrl, checksumUrl);
    }

    public static async Task DownloadVerifyAndRestartAsync(
        AppUpdateInfo update,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var currentExecutable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExecutable) || !File.Exists(currentExecutable))
            throw new InvalidOperationException("The running application path could not be determined.");

        var updateDirectory = Path.Combine(Path.GetTempPath(), "SiegeCrashScannerUpdate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(updateDirectory);
        var downloadedExecutable = Path.Combine(updateDirectory, ExecutableAssetName);

        try
        {
            progress?.Report($"Downloading Siege Crash Scanner {update.Version}…");
            using var client = CreateClient();
            var executableBytes = await client.GetByteArrayAsync(update.ExecutableUrl, cancellationToken);
            var checksumText = await client.GetStringAsync(update.ChecksumUrl, cancellationToken);
            await File.WriteAllBytesAsync(downloadedExecutable, executableBytes, cancellationToken);

            progress?.Report("Verifying the downloaded update…");
            var match = Regex.Match(checksumText, @"(?i)\b[a-f0-9]{64}\b");
            if (!match.Success) throw new InvalidDataException("The release checksum file is invalid.");
            var actualHash = Convert.ToHexString(SHA256.HashData(executableBytes));
            if (!actualHash.Equals(match.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The downloaded update did not match its published SHA-256 checksum.");

            progress?.Report("Update verified. Waiting for administrator approval…");
            StartReplacementProcess(downloadedExecutable, currentExecutable, updateDirectory);
        }
        catch
        {
            try { Directory.Delete(updateDirectory, true); } catch { }
            throw;
        }
    }

    private static void StartReplacementProcess(string source, string target, string updateDirectory)
    {
        var processId = Environment.ProcessId;
        var escapedSource = source.Replace("'", "''");
        var escapedTarget = target.Replace("'", "''");
        var escapedDirectory = updateDirectory.Replace("'", "''");
        var script = $$"""
            Wait-Process -Id {{processId}} -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 700
            Copy-Item -LiteralPath '{{escapedSource}}' -Destination '{{escapedTarget}}' -Force
            Start-Process -FilePath '{{escapedTarget}}'
            Start-Sleep -Seconds 1
            Remove-Item -LiteralPath '{{escapedDirectory}}' -Recurse -Force -ErrorAction SilentlyContinue
            """;

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = ProcessTools.PowerShellArguments(script),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("The update installer could not be started.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SiegeCrashScanner", CurrentVersion.ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = string.Empty;
    }
}
