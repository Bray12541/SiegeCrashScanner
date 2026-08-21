using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace SiegeCrashScanner;

internal static partial class SiegeLocator
{
    private static readonly string[] Executables = ["RainbowSix.exe", "RainbowSix_Vulkan.exe", "RainbowSix_DX12.exe"];

    public static void FillInstallation(SystemSnapshot snapshot)
    {
        try
        {
            var candidates = new List<string>();
            AddRegistryCandidates(candidates);
            AddSteamCandidates(candidates);
            AddCommonCandidates(candidates);

            var found = candidates.Select(NormalizeCandidate).FirstOrDefault(IsSiegeDirectory);
            if (found is null) return;
            snapshot.SiegeInstallDirectory = found;
            var root = Path.GetPathRoot(found);
            if (!string.IsNullOrWhiteSpace(root)) snapshot.SiegeDriveFreeBytes = new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            snapshot.CollectionNotes.Add("Siege installation detection was incomplete: " + ex.Message);
        }
    }

    private static void AddRegistryCandidates(List<string> candidates)
    {
        string[] uninstallRoots = [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        ];
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var path in uninstallRoots)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var item = root.OpenSubKey(name);
                var display = item?.GetValue("DisplayName") as string ?? string.Empty;
                if (!display.Contains("Rainbow Six", StringComparison.OrdinalIgnoreCase) && !display.Contains("Siege", StringComparison.OrdinalIgnoreCase)) continue;
                if (item?.GetValue("InstallLocation") is string location) candidates.Add(location);
            }
        }

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var installs = baseKey.OpenSubKey(@"SOFTWARE\Ubisoft\Launcher\Installs");
            if (installs is null) continue;
            foreach (var id in installs.GetSubKeyNames())
            {
                using var game = installs.OpenSubKey(id);
                if (game?.GetValue("InstallDir") is string directory) candidates.Add(directory);
                if (game?.GetValue("InstallLocation") is string location) candidates.Add(location);
            }
        }
    }

    private static void AddSteamCandidates(List<string> candidates)
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in new[] {
            (Registry.CurrentUser, @"SOFTWARE\Valve\Steam"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam") })
        {
            using var key = pair.Item1.OpenSubKey(pair.Item2);
            var path = key?.GetValue("SteamPath") as string ?? key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path)) steamRoots.Add(path.Replace('/', '\\'));
        }
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles)) steamRoots.Add(Path.Combine(programFiles, "Steam"));

        foreach (var root in steamRoots)
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                var text = File.ReadAllText(vdf);
                foreach (Match match in SteamPathRegex().Matches(text)) libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
            }
            foreach (var library in libraries)
            {
                var common = Path.Combine(library, "steamapps", "common");
                candidates.Add(Path.Combine(common, "Tom Clancy's Rainbow Six Siege"));
                var manifest = Path.Combine(library, "steamapps", "appmanifest_359550.acf");
                if (File.Exists(manifest))
                {
                    var match = InstallDirRegex().Match(File.ReadAllText(manifest));
                    if (match.Success) candidates.Add(Path.Combine(common, match.Groups[1].Value));
                }
            }
        }
    }

    private static void AddCommonCandidates(List<string> candidates)
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "Ubisoft", "Ubisoft Game Launcher", "games", "Tom Clancy's Rainbow Six Siege"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Program Files", "Ubisoft", "Ubisoft Game Launcher", "games", "Tom Clancy's Rainbow Six Siege"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Ubisoft Games", "Tom Clancy's Rainbow Six Siege"));
            candidates.Add(Path.Combine(drive.RootDirectory.FullName, "SteamLibrary", "steamapps", "common", "Tom Clancy's Rainbow Six Siege"));
        }
    }

    private static string NormalizeCandidate(string value) => Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar);
    private static bool IsSiegeDirectory(string directory) => Directory.Exists(directory) && Executables.Any(name => File.Exists(Path.Combine(directory, name)));

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPathRegex();

    [GeneratedRegex("\\\"installdir\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex InstallDirRegex();
}
