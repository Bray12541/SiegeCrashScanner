using System.Diagnostics;

namespace SiegeCrashScanner;

internal static class SoftwareScanner
{
    private static readonly (string Display, string[] Processes)[] KnownPrograms =
    [
        ("Discord / Discord Overlay", ["Discord", "DiscordCanary", "DiscordPTB"]),
        ("NVIDIA App / GeForce Experience", ["NVIDIAApp", "NVIDIA App", "NVIDIA GeForce Experience", "NVIDIA Share", "NVIDIA Overlay", "NVIDIA Web Helper"]),
        ("AMD Software", ["RadeonSoftware", "AMDRSServ", "amdow"]),
        ("Xbox Game Bar", ["GameBar", "GameBarFTServer", "XboxGameBarWidgets"]),
        ("MSI Afterburner", ["MSIAfterburner"]),
        ("RivaTuner Statistics Server", ["RTSS", "RTSSHooksLoader64"]),
        ("Overwolf", ["Overwolf", "OverwolfBrowser"]),
        ("Medal", ["Medal", "MedalEncoder"]),
        ("SteelSeries GG", ["SteelSeriesGG", "SteelSeriesEngine", "SteelSeriesPrism"]),
        ("Corsair iCUE", ["iCUE", "iCUEDevicePluginHost"]),
        ("Razer Synapse", ["Razer Synapse", "RazerAppEngine", "RazerCentral"]),
        ("Logitech G Hub", ["lghub", "lghub_agent", "lghub_updater"])
    ];

    public static List<string> FindRunning()
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try { running.Add(process.ProcessName); }
            catch { }
            finally { process.Dispose(); }
        }
        return KnownPrograms.Where(program => program.Processes.Any(running.Contains)).Select(program => program.Display).ToList();
    }
}
