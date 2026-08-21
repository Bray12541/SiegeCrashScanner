using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SiegeCrashScanner;

internal static partial class VendorUpdateService
{
    private static readonly Uri NvidiaAppPage = new("https://www.nvidia.com/en-us/software/nvidia-app/");
    private static readonly Uri NvidiaInstallerFallback = new("https://us.download.nvidia.com/nvapp/client/11.0.8.299/NVIDIA_app_v11.0.8.299.exe");
    private static readonly Uri IntelDsaInstaller = new("https://dsadata.intel.com/installer");
    private static readonly Uri IntelDsaDashboard = new("https://www.intel.com/content/www/us/en/support/detect.html");

    public static async Task<string> OpenNvidiaUpdaterAsync(IProgress<string>? progress = null)
    {
        var installed = FindNvidiaApp();
        if (installed is not null)
        {
            progress?.Report("Opening the installed NVIDIA App…");
            Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
            return "NVIDIA vendor updater\n\nOpened the installed NVIDIA App. Select Drivers, choose the Game Ready Driver, and approve Download/Install in NVIDIA's window.";
        }

        progress?.Report("Locating the current official NVIDIA App installer…");
        var downloadUri = await FindCurrentNvidiaInstallerAsync();
        var result = await DownloadVerifyAndRunAsync(downloadUri, "NVIDIA", "NVIDIA-App-Installer.exe", progress);
        return "NVIDIA vendor updater\n\n" + result + "\n\nAfter setup finishes, open NVIDIA App → Drivers and approve the Game Ready Driver installation.";
    }

    public static async Task<string> OpenIntelUpdaterAsync(IProgress<string>? progress = null)
    {
        var installed = FindIntelDsa();
        if (installed is not null)
        {
            progress?.Report("Opening Intel Driver & Support Assistant…");
            Process.Start(new ProcessStartInfo(installed) { UseShellExecute = true });
            await Task.Delay(1200);
            Process.Start(new ProcessStartInfo(IntelDsaDashboard.AbsoluteUri) { UseShellExecute = true });
            return "Intel platform updater\n\nOpened Intel Driver & Support Assistant. Review its detected platform updates and approve Install all in Intel's dashboard. Some motherboard-specific chipset or Management Engine packages may still come only from the PC/motherboard manufacturer.";
        }

        var result = await DownloadVerifyAndRunAsync(IntelDsaInstaller, "Intel", "Intel-DSA-Installer.exe", progress);
        Process.Start(new ProcessStartInfo(IntelDsaDashboard.AbsoluteUri) { UseShellExecute = true });
        return "Intel platform updater\n\n" + result + "\n\nIntel's dashboard has been opened. Review its detected updates and approve installation there.";
    }

    private static async Task<Uri> FindCurrentNvidiaInstallerAsync()
    {
        try
        {
            using var client = CreateHttpClient();
            var html = await client.GetStringAsync(NvidiaAppPage);
            var match = NvidiaInstallerRegex().Match(WebUtility.HtmlDecode(html));
            if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) && IsOfficialHost(uri, "nvidia.com")) return uri;
        }
        catch { }
        return NvidiaInstallerFallback;
    }

    private static async Task<string> DownloadVerifyAndRunAsync(
        Uri source,
        string expectedPublisher,
        string fileName,
        IProgress<string>? progress)
    {
        var officialDomain = expectedPublisher.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "nvidia.com" : "intel.com";
        if (!IsOfficialHost(source, officialDomain)) throw new InvalidOperationException($"Refused a download outside the official {officialDomain} domain.");

        var directory = Path.Combine(Path.GetTempPath(), "SiegeCrashScanner", "VendorInstallers", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, fileName);
        try
        {
            progress?.Report($"Downloading from {source.Host}…");
            using var client = CreateHttpClient();
            using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is { } finalUri && !IsOfficialHost(finalUri, officialDomain))
                throw new InvalidOperationException("The vendor download redirected outside its official domain.");

            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0) progress?.Report($"Downloading official installer… {downloaded * 100d / total:0}%");
                }
            }

            progress?.Report("Verifying the Windows Authenticode signature…");
            var signature = await ReadSignatureAsync(destination);
            if (!signature.Status.Equals("Valid", StringComparison.OrdinalIgnoreCase) ||
                !signature.Subject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The installer signature was not valid for {expectedPublisher}. Status: {signature.Status}; signer: {signature.Subject}.");

            await using (var stream = File.OpenRead(destination))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
                progress?.Report($"Verified {expectedPublisher}. SHA-256 {hash[..12]}… Starting installer…");
            }

            var startInfo = new ProcessStartInfo(destination) { UseShellExecute = true, Verb = "runas" };
            using var installer = Process.Start(startInfo) ?? throw new InvalidOperationException("The official vendor installer could not be started.");
            await installer.WaitForExitAsync();
            return $"Official installer signer verified: {signature.Subject}. Installer exit code: {installer.ExitCode}.";
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
    }

    private static async Task<(string Status, string Subject)> ReadSignatureAsync(string file)
    {
        var escaped = file.Replace("'", "''");
        var script = $"$signature=Get-AuthenticodeSignature -LiteralPath '{escaped}'; [pscustomobject]@{{Status=$signature.Status.ToString();Subject=$signature.SignerCertificate.Subject}} | ConvertTo-Json -Compress";
        var result = await ProcessTools.RunAsync("powershell.exe", ProcessTools.PowerShellArguments(script));
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            throw new InvalidDataException("Windows could not validate the vendor installer's digital signature.");
        using var document = JsonDocument.Parse(result.Output.Trim());
        var root = document.RootElement;
        return (root.GetProperty("Status").GetString() ?? "Unknown", root.GetProperty("Subject").GetString() ?? "Unknown");
    }

    private static string? FindNvidiaApp() => FindExecutable(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Path.Combine("NVIDIA Corporation", "NVIDIA app"),
        "NVIDIA app.exe");

    private static string? FindIntelDsa()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        foreach (var root in roots)
        {
            var found = FindExecutable(root, Path.Combine("Intel", "Driver and Support Assistant"), "DSATray.exe");
            if (found is not null) return found;
        }
        return null;
    }

    private static string? FindExecutable(string root, string relativeDirectory, string name)
    {
        if (string.IsNullOrWhiteSpace(root)) return null;
        var directory = Path.Combine(root, relativeDirectory);
        if (!Directory.Exists(directory)) return null;
        try { return Directory.EnumerateFiles(directory, name, SearchOption.AllDirectories).FirstOrDefault(); }
        catch { return null; }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) SiegeCrashScanner/1.3");
        return client;
    }

    private static bool IsOfficialHost(Uri uri, string domain) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("https://us\\.download\\.nvidia\\.com/nvapp/client/[^\\\"'<>\\s]+\\.exe", RegexOptions.IgnoreCase)]
    private static partial Regex NvidiaInstallerRegex();
}
