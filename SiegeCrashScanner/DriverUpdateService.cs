namespace SiegeCrashScanner;

internal static class DriverUpdateService
{
    public static async Task<string> InstallApplicableUpdatesAsync(
        SystemSnapshot system,
        IProgress<string>? progress = null)
    {
        var vendors = DetectVendors(system);
        var vendorList = string.Join(",", vendors.Select(v => $"'{v.Replace("'", "''")}'"));

        var script = $$"""
            $ErrorActionPreference = 'Stop'
            Write-Output 'Searching Windows Update for compatible signed drivers...'
            $vendors = @({{vendorList}})
            $session = New-Object -ComObject Microsoft.Update.Session
            $session.ClientApplicationID = 'Siege Crash Scanner'
            $searcher = $session.CreateUpdateSearcher()
            $search = $searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0")
            $selected = New-Object -ComObject Microsoft.Update.UpdateColl

            foreach ($update in @($search.Updates)) {
                $title = [string]$update.Title
                $matchesHardware = $false
                foreach ($vendor in $vendors) {
                    if ($title -match [regex]::Escape($vendor)) { $matchesHardware = $true; break }
                }
                if ($title -match '(?i)display|graphics|processor|chipset|management engine|system') {
                    $matchesHardware = $true
                }
                if (-not $matchesHardware) { continue }
                if (-not $update.EulaAccepted) { $update.AcceptEula() }
                [void]$selected.Add($update)
                Write-Output ("FOUND: " + $title)
            }

            if ($selected.Count -eq 0) {
                Write-Output ''
                Write-Output 'No applicable GPU or platform-driver updates were offered by Windows Update.'
                exit 0
            }

            Write-Output ''
            Write-Output ("Downloading " + $selected.Count + " driver update(s)...")
            $downloader = $session.CreateUpdateDownloader()
            $downloader.Updates = $selected
            $download = $downloader.Download()
            if ($download.ResultCode -notin 2,3) {
                throw "Driver download failed. Windows Update result code: $($download.ResultCode)"
            }

            $ready = New-Object -ComObject Microsoft.Update.UpdateColl
            foreach ($update in @($selected)) {
                if ($update.IsDownloaded) { [void]$ready.Add($update) }
                else { Write-Output ("NOT DOWNLOADED: " + $update.Title) }
            }
            if ($ready.Count -eq 0) { throw 'Windows did not download any of the selected drivers.' }

            Write-Output ("Installing " + $ready.Count + " driver update(s)...")
            $installer = $session.CreateUpdateInstaller()
            $installer.Updates = $ready
            $install = $installer.Install()
            $resultNames = @('Not started','In progress','Succeeded','Succeeded with errors','Failed','Aborted')
            for ($i = 0; $i -lt $ready.Count; $i++) {
                $code = [int]$install.GetUpdateResult($i).ResultCode
                $name = if ($code -ge 0 -and $code -lt $resultNames.Count) { $resultNames[$code] } else { "Code $code" }
                Write-Output ("$name`: " + $ready.Item($i).Title)
            }
            Write-Output ''
            if ($install.RebootRequired) {
                Write-Output 'RESTART REQUIRED: Save your work and restart Windows before testing Siege.'
            } else {
                Write-Output 'Driver update completed. Windows did not request a restart.'
            }
            exit $(if ($install.ResultCode -in 2,3) { 0 } else { 1 })
            """;

        var result = await ProcessTools.RunElevatedPowerShellAsync(
            script,
            progress,
            "Windows is searching, downloading, and installing drivers. Keep the PC powered on…");

        return $"GPU & platform driver update\n\n{result.Output.Trim()}\n\nExit code: {result.ExitCode}";
    }

    private static string[] DetectVendors(SystemSnapshot system)
    {
        var combined = system.Cpu + " " + system.Gpu;
        var vendors = new List<string>();
        if (combined.Contains("Intel", StringComparison.OrdinalIgnoreCase)) vendors.Add("Intel");
        if (combined.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) vendors.Add("NVIDIA");
        if (combined.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            vendors.Add("AMD");
            vendors.Add("Advanced Micro Devices");
        }
        return vendors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
