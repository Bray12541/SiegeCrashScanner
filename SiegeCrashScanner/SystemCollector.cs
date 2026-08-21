using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SiegeCrashScanner;

internal static class SystemCollector
{
    public static async Task<(SystemSnapshot Snapshot, BattleEyeResult BattleEye)> CollectAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = new SystemSnapshot();
        var battleEye = new BattleEyeResult();
        FillMemory(snapshot);

        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
            $gpus = @(Get-CimInstance Win32_VideoController | ForEach-Object {
                [pscustomobject]@{ Name=$_.Name; DriverVersion=$_.DriverVersion; DriverDate=if ($_.DriverDate) { $_.DriverDate.ToString('yyyy-MM-dd') } else { '' } }
            })
            $memory = @(Get-CimInstance Win32_PhysicalMemory)
            $board = Get-CimInstance Win32_BaseBoard | Select-Object -First 1
            $bios = Get-CimInstance Win32_BIOS | Select-Object -First 1
            $os = Get-CimInstance Win32_OperatingSystem
            $page = @(Get-CimInstance Win32_PageFileUsage)
            $be = Get-CimInstance Win32_Service | Where-Object { $_.Name -match '^(BE|BEService)' -or $_.DisplayName -match 'BattlEye' } | Select-Object -First 1
            [pscustomobject]@{
                Cpu=$cpu.Name
                Gpus=$gpus
                RamSpeeds=@($memory | ForEach-Object { if ($_.ConfiguredClockSpeed) { $_.ConfiguredClockSpeed } elseif ($_.Speed) { $_.Speed } } | Sort-Object -Unique)
                BoardManufacturer=$board.Manufacturer
                BoardProduct=$board.Product
                Bios=$bios.SMBIOSBIOSVersion
                WindowsCaption=$os.Caption
                WindowsVersion=$os.Version
                WindowsBuild=$os.BuildNumber
                PagefileSizeMb=($page | Measure-Object -Property AllocatedBaseSize -Sum).Sum
                BattleEyeInstalled=($null -ne $be)
                BattleEyeState=if ($be) { $be.State } else { 'Missing' }
            } | ConvertTo-Json -Depth 5 -Compress
            """;

        try
        {
            var result = await ProcessTools.RunAsync("powershell.exe", ProcessTools.PowerShellArguments(script), cancellationToken);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Windows system query returned no data." : result.Error.Trim());

            using var doc = JsonDocument.Parse(result.Output.Trim());
            var root = doc.RootElement;
            snapshot.Cpu = Text(root, "Cpu", "Unknown CPU");
            snapshot.WindowsVersion = $"{Text(root, "WindowsCaption", "Windows")} {Text(root, "WindowsVersion", string.Empty)} (Build {Text(root, "WindowsBuild", "unknown")})".Trim();
            snapshot.Motherboard = $"{Text(root, "BoardManufacturer", string.Empty)} {Text(root, "BoardProduct", "Unknown")}".Trim();
            snapshot.BiosVersion = Text(root, "Bios", "Unknown");
            snapshot.PagefileSizeMb = Number(root, "PagefileSizeMb");
            snapshot.PagefileEnabled = snapshot.PagefileSizeMb > 0;

            if (root.TryGetProperty("RamSpeeds", out var speeds))
            {
                var values = Elements(speeds).Select(e => e.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
                snapshot.RamSpeed = values.Length == 0 ? "Unknown" : string.Join(" / ", values.Select(v => v + " MT/s"));
            }

            if (root.TryGetProperty("Gpus", out var gpus))
            {
                var gpuItems = Elements(gpus).Where(g => g.ValueKind == JsonValueKind.Object).ToList();
                var preferred = gpuItems.FirstOrDefault(g => !Text(g, "Name", "").Contains("Microsoft", StringComparison.OrdinalIgnoreCase));
                if (preferred.ValueKind != JsonValueKind.Object && gpuItems.Count > 0) preferred = gpuItems[0];
                snapshot.Gpu = gpuItems.Count == 0 ? "Unknown GPU" : string.Join(" + ", gpuItems.Select(g => Text(g, "Name", "Unknown")).Distinct());
                if (preferred.ValueKind == JsonValueKind.Object)
                {
                    snapshot.GpuDriverVersion = Text(preferred, "DriverVersion", "Unknown");
                    snapshot.GpuDriverDate = Text(preferred, "DriverDate", "Unknown");
                }
            }

            battleEye.Installed = Bool(root, "BattleEyeInstalled");
            battleEye.State = Text(root, "BattleEyeState", battleEye.Installed ? "Unknown" : "Missing");
        }
        catch (Exception ex)
        {
            snapshot.CollectionNotes.Add("Some hardware details could not be read: " + ex.Message);
            snapshot.Cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Unknown CPU";
            snapshot.WindowsVersion = $"Windows {Environment.OSVersion.Version} ({RuntimeInformation.OSDescription})";
        }

        SiegeLocator.FillInstallation(snapshot);
        return (snapshot, battleEye);
    }

    private static void FillMemory(SystemSnapshot snapshot)
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return;
        snapshot.TotalRamBytes = status.TotalPhys;
        snapshot.AvailableRamBytes = status.AvailPhys;
        snapshot.CommitLimitBytes = status.TotalPageFile;
        snapshot.CommitUsedBytes = status.TotalPageFile > status.AvailPageFile ? status.TotalPageFile - status.AvailPageFile : 0;
    }

    private static IEnumerable<JsonElement> Elements(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray(),
        JsonValueKind.Null or JsonValueKind.Undefined => [],
        _ => [element]
    };

    private static string Text(JsonElement element, string property, string fallback) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null && !string.IsNullOrWhiteSpace(value.ToString()) ? value.ToString() : fallback;

    private static long Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && long.TryParse(value.ToString(), out var number) ? number : 0;

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True || bool.TryParse(value.ToString(), out var result) && result);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
