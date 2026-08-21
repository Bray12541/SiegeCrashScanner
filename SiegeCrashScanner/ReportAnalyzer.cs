namespace SiegeCrashScanner;

internal static class ReportAnalyzer
{
    private const ulong Gb = 1024UL * 1024 * 1024;

    public static void Analyze(ScanReport report)
    {
        AddFindings(report);
        AddCauses(report);
        report.LikelyCauses.Sort((a, b) => b.RankScore.CompareTo(a.RankScore));
        if (report.LikelyCauses.Count > 5) report.LikelyCauses.RemoveRange(5, report.LikelyCauses.Count - 5);
        report.RecommendedNextStep = ChooseNextStep(report);
    }

    private static void AddFindings(ScanReport report)
    {
        report.Findings.Add(new Finding
        {
            Title = "Siege Crash Events",
            Status = report.SiegeCrashes.Count > 0 ? FindingStatus.Warning : FindingStatus.Info,
            Detail = report.SiegeCrashes.Count > 0
                ? $"Found {report.SiegeCrashes.Count} Siege crash event(s) from the last 30 days. Newest: {report.SiegeCrashes[0].Time:g}."
                : "No Siege application crashes were found in the last 30 days."
        });

        var memory = report.MemoryDiagnostic;
        report.Findings.Add(new Finding
        {
            Title = "Memory Test",
            Status = memory.Passed switch { true => FindingStatus.Pass, false => FindingStatus.Fail, _ => FindingStatus.Info },
            Detail = memory.Detail + (memory.TestDate is null ? string.Empty : $" Last test: {memory.TestDate:g}.")
        });

        var severeWhea = report.WheaEvents.Any(e => e.Severity.Equals("Fatal", StringComparison.OrdinalIgnoreCase));
        var persistedWhea = report.WheaEvents.Count(e => e.IsPreviousError);
        var correlatedWhea = report.CorrelatedWheaEvents.Count;
        report.Findings.Add(new Finding
        {
            Title = "WHEA Hardware Errors",
            Status = report.RawWheaEventCount == 0 ? FindingStatus.Pass : severeWhea ? FindingStatus.Fail : FindingStatus.Warning,
            Detail = report.RawWheaEventCount == 0
                ? "No recent WHEA hardware errors found."
                : $"Found {report.RawWheaEventCount} log occurrence(s), representing {report.WheaEvents.Count} unique record(s): {string.Join(", ", report.WheaEvents.Select(e => e.Category).Distinct())}. " +
                  (persistedWhea > 0 ? $"{persistedWhea} unique record(s) were marked as persisted PreviousError reports. " : string.Empty) +
                  (correlatedWhea > 0 ? $"{correlatedWhea} record(s) occurred within five minutes of a Siege crash." : "No unique WHEA record was within five minutes of a recorded Siege crash.")
        });

        var availablePercent = report.System.TotalRamBytes == 0 ? 100 : report.System.AvailableRamBytes * 100d / report.System.TotalRamBytes;
        var lowRam = report.System.AvailableRamBytes < 2 * Gb || availablePercent < 5;
        report.Findings.Add(new Finding
        {
            Title = "RAM Availability",
            Status = lowRam ? FindingStatus.Warning : FindingStatus.Pass,
            Detail = $"{Format.Bytes(report.System.UsedRamBytes)} in use, {Format.Bytes(report.System.AvailableRamBytes)} available of {Format.Bytes(report.System.TotalRamBytes)}. " +
                     (lowRam ? "Windows is extremely low on available memory." : "Available memory is not critically low.")
        });

        var unhealthyDisks = report.System.StorageDevices.Where(device =>
            !device.Status.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
            !device.Status.Equals("Unknown", StringComparison.OrdinalIgnoreCase)).ToList();
        var severeStorageEvent = report.StorageEvents.Any(item => item.EventId is 7 or 11 or 55);
        report.Findings.Add(new Finding
        {
            Title = "Storage Health",
            Status = unhealthyDisks.Count > 0 || severeStorageEvent ? FindingStatus.Fail : report.StorageEvents.Count > 0 ? FindingStatus.Warning : FindingStatus.Pass,
            Detail = unhealthyDisks.Count > 0
                ? $"Windows reports a non-OK status for: {string.Join(", ", unhealthyDisks.Select(d => d.Model))}."
                : report.StorageEvents.Count > 0
                    ? $"Drive status reports OK/unknown, but Windows recorded {report.StorageEvents.Count} recent disk, controller, or filesystem event(s)."
                    : $"No recent disk/controller errors were found. Windows reports {report.System.StorageDevices.Count} physical drive(s) with OK or unknown status."
        });

        report.Findings.Add(new Finding
        {
            Title = "System Stability",
            Status = report.StabilityEvents.Count > 0 ? FindingStatus.Warning : FindingStatus.Pass,
            Detail = report.StabilityEvents.Count > 0
                ? $"Windows recorded {report.StabilityEvents.Count} unexpected shutdown or Kernel-Power event(s) in the last 30 days. These events show an unclean shutdown but do not identify its cause."
                : "No unexpected shutdown or Kernel-Power events were found in the last 30 days."
        });

        report.Findings.Add(new Finding
        {
            Title = "BIOS / Firmware",
            Status = FindingStatus.Info,
            Detail = $"Installed BIOS: {report.System.BiosVersion} ({report.System.BiosDate}). The scanner does not declare it outdated without comparing the exact motherboard revision against its manufacturer."
        });

        if (report.Comparison.PreviousScanTime is not null)
        {
            report.Findings.Add(new Finding
            {
                Title = "Since Previous Scan",
                Status = report.Comparison.NewWheaOccurrences + report.Comparison.NewGpuEvents + report.Comparison.NewStorageEvents + report.Comparison.NewSiegeCrashes == 0 ? FindingStatus.Pass : FindingStatus.Warning,
                Detail = $"Since {report.Comparison.PreviousScanTime:g}: {report.Comparison.NewSiegeCrashes} Siege crash(es), {report.Comparison.NewWheaOccurrences} WHEA occurrence(s), {report.Comparison.NewGpuEvents} GPU event(s), and {report.Comparison.NewStorageEvents} storage event(s). This comparison lasts for the current app session only."
            });
        }

        var commit = report.System.CommitLimitBytes == 0 ? 0 : report.System.CommitUsedBytes * 100d / report.System.CommitLimitBytes;
        report.Findings.Add(new Finding
        {
            Title = "Pagefile",
            Status = report.System.PagefileEnabled ? FindingStatus.Pass : FindingStatus.Warning,
            Detail = report.System.PagefileEnabled
                ? $"Windows virtual memory is enabled ({Format.Megabytes(report.System.PagefileSizeMb)} allocated). Current commit: {Format.Bytes(report.System.CommitUsedBytes)} / {Format.Bytes(report.System.CommitLimitBytes)} ({commit:0}%)."
                : $"The Windows pagefile appears to be disabled. Current commit: {Format.Bytes(report.System.CommitUsedBytes)} / {Format.Bytes(report.System.CommitLimitBytes)}."
        });

        if (report.CorrelatedGpuEvents.Count > 0)
        {
            var nearest = report.CorrelatedGpuEvents.OrderBy(e => e.SecondsApart).First();
            report.Findings.Add(new Finding
            {
                Title = "GPU Driver",
                Status = FindingStatus.Warning,
                Detail = $"A {nearest.DriverEvent.Provider} graphics-driver event occurred {nearest.SecondsApart:0} seconds from a Siege crash."
            });
        }
        else
        {
            report.Findings.Add(new Finding
            {
                Title = "GPU Driver",
                Status = report.GpuEvents.Count > 0 ? FindingStatus.Warning : FindingStatus.Pass,
                Detail = report.GpuEvents.Count > 0
                    ? $"Found {report.GpuEvents.Count} recent graphics-driver event(s), but none were within two minutes of a recorded Siege crash."
                    : "No recent NVIDIA/AMD/Display driver errors were found."
            });
        }

        report.Findings.Add(new Finding
        {
            Title = "Siege Installation",
            Status = !report.System.SiegeDetected ? FindingStatus.Warning : report.System.SiegeDriveFreeBytes >= 0 && report.System.SiegeDriveFreeBytes < 15 * (long)Gb ? FindingStatus.Warning : FindingStatus.Pass,
            Detail = !report.System.SiegeDetected ? "Rainbow Six Siege was not detected in common Steam or Ubisoft Connect locations."
                : report.System.SiegeDriveFreeBytes < 15 * (long)Gb ? $"Detected at {report.System.SiegeInstallDirectory}. Only {Format.Bytes((ulong)Math.Max(0, report.System.SiegeDriveFreeBytes))} is free on its drive; 15 GB or more is recommended."
                : $"Detected at {report.System.SiegeInstallDirectory}. Its drive has {Format.Bytes((ulong)report.System.SiegeDriveFreeBytes)} free."
        });

        var beState = report.BattleEye.State;
        var beHealthy = report.BattleEye.Installed && beState.Equals("Running", StringComparison.OrdinalIgnoreCase) && report.BattleEye.Failures.Count == 0;
        report.Findings.Add(new Finding
        {
            Title = "BattlEye",
            Status = beHealthy ? FindingStatus.Pass : report.BattleEye.Failures.Count > 0 ? FindingStatus.Warning : FindingStatus.Info,
            Detail = !report.BattleEye.Installed ? "BattlEye service is missing (it may only be installed or started when the game launches)."
                : report.BattleEye.Failures.Count > 0 ? $"BattlEye is {beState.ToLowerInvariant()}, with {report.BattleEye.Failures.Count} recent service failure event(s)."
                : $"BattlEye service is installed and {beState.ToLowerInvariant()}."
        });

        report.Findings.Add(new Finding
        {
            Title = "Possible Software Conflicts",
            Status = report.SoftwareConflicts.Count == 0 ? FindingStatus.Pass : FindingStatus.Warning,
            Detail = report.SoftwareConflicts.Count == 0
                ? "No common overlay or monitoring applications from the scan list are currently running."
                : string.Join(", ", report.SoftwareConflicts) + " are running. Their presence is not proof that they caused a crash."
        });
    }

    private static void AddCauses(ScanReport report)
    {
        if (report.MemoryDiagnostic.Passed == false)
            Add(report, "Memory instability", "High", "Windows Memory Diagnostic reported errors.", 100);

        if (report.RawWheaEventCount > 0)
        {
            var memory = report.WheaEvents.Any(e => e.Category.Contains("Memory", StringComparison.OrdinalIgnoreCase));
            var cpu = report.WheaEvents.Any(e => e.Category.Contains("CPU", StringComparison.OrdinalIgnoreCase));
            var firmware = report.WheaEvents.Any(e => e.Category.Contains("firmware", StringComparison.OrdinalIgnoreCase));
            var uniqueFatal = report.WheaEvents.Count(e => e.Severity.Equals("Fatal", StringComparison.OrdinalIgnoreCase));
            var highConfidence = report.CorrelatedWheaEvents.Count > 0 || uniqueFatal > 1;
            Add(report, memory ? "Memory controller or RAM instability" : cpu ? "CPU or platform hardware instability" : firmware ? "BIOS or platform-firmware instability" : "Hardware/PCIe instability",
                highConfidence ? "High" : "Medium",
                $"Windows recorded {report.RawWheaEventCount} WHEA occurrence(s), representing {report.WheaEvents.Count} unique record(s)" +
                (report.WheaEvents.Any(e => e.IsPreviousError) ? "; some were persisted PreviousError reports and may be replayed across boots." : "."),
                highConfidence ? 95 : 75);
        }

        if (report.CorrelatedGpuEvents.Count > 0)
            Add(report, "GPU driver issue", "High", "A graphics-driver event occurred within two minutes of a Siege crash.", 98);
        else if (report.GpuEvents.Count > 0)
            Add(report, "GPU driver issue", "Medium", "Graphics-driver errors exist, although none line up closely with a recorded Siege crash.", 62);

        var availablePercent = report.System.TotalRamBytes == 0 ? 100 : report.System.AvailableRamBytes * 100d / report.System.TotalRamBytes;
        if (report.System.AvailableRamBytes < 2 * Gb || availablePercent < 5)
            Add(report, "Memory pressure", "Medium", "Available physical memory was critically low during this scan.", 68);

        if (!report.System.PagefileEnabled)
            Add(report, "Disabled virtual memory", "Medium", "No allocated Windows pagefile was detected.", 72);

        if (report.System.SiegeDetected && report.System.SiegeDriveFreeBytes >= 0 && report.System.SiegeDriveFreeBytes < 15 * (long)Gb)
            Add(report, "Low free disk space", "Medium", "The Siege drive has less than 15 GB free.", 58);

        if (report.StorageEvents.Count > 0)
            Add(report, "Storage or filesystem instability", report.StorageEvents.Any(item => item.EventId is 7 or 11 or 55) ? "High" : "Medium",
                $"Windows recorded {report.StorageEvents.Count} recent disk, controller, or filesystem event(s).", 82);

        if (report.BattleEye.Failures.Count > 0)
            Add(report, "BattlEye service issue", "Medium", "Windows recorded recent BattlEye service failures.", 66);
        else if (!report.BattleEye.Installed && report.System.SiegeDetected)
            Add(report, "BattlEye installation issue", "Low", "Siege was detected but the BattlEye service was not found.", 36);

        if (report.SoftwareConflicts.Count > 0)
            Add(report, "Overlay or monitoring-software conflict", report.SoftwareConflicts.Count >= 3 ? "Medium" : "Low",
                $"{report.SoftwareConflicts.Count} common overlay/monitoring application(s) were running when scanned; this is only a testable possibility.", report.SoftwareConflicts.Count >= 3 ? 52 : 34);

        var corruptionCrashes = report.SiegeCrashes.Where(c => c.ExceptionCode is "0xc0000005" or "0xc0000374" or "0xc0000409").ToList();
        if (corruptionCrashes.Count > 0 && report.MemoryDiagnostic.Passed != false && !report.WheaEvents.Any(e => e.Category.Contains("Memory", StringComparison.OrdinalIgnoreCase)))
            Add(report, "Application memory corruption signal", "Low", $"{corruptionCrashes.Count} crash(es) used an access/heap/security exception code, but this does not establish bad RAM.", 42);

        var gameModuleCrashes = report.SiegeCrashes.Count(c => c.FaultingModule.Contains("RainbowSix", StringComparison.OrdinalIgnoreCase));
        if (gameModuleCrashes >= 2)
            Add(report, "Siege installation or game-code issue", "Medium", $"The Siege executable itself was the faulting module in {gameModuleCrashes} crashes.", 54);
    }

    private static void Add(ScanReport report, string name, string likelihood, string evidence, int score)
    {
        if (report.LikelyCauses.Any(c => c.Name == name)) return;
        report.LikelyCauses.Add(new LikelyCause { Name = name, Likelihood = likelihood, Evidence = evidence, RankScore = score });
    }

    private static string ChooseNextStep(ScanReport report)
    {
        if (report.MemoryDiagnostic.Passed == false)
            return "Run the Windows Memory Diagnostic extended test once, then compare its new result before changing any BIOS or RAM settings.";
        if (report.CorrelatedGpuEvents.Count > 0)
            return "Perform one clean reinstall of the current GPU driver using the GPU vendor's official installer, then test Siege again.";
        if (report.WheaEvents.Any(item => item.Category.Contains("firmware", StringComparison.OrdinalIgnoreCase)))
            return "Update the exact motherboard model and revision to its latest stable BIOS using the manufacturer's built-in firmware utility, then test Siege once and scan again for new WHEA timestamps.";
        if (report.WheaEvents.Count > 0)
            return "Run your PC or motherboard manufacturer's hardware diagnostic once and compare its result with the decoded WHEA category shown above.";
        if (report.StorageEvents.Count > 0)
            return "Run the app's CHKDSK online scan once, then review whether it reports filesystem or drive errors before attempting repairs.";
        if (!report.System.PagefileEnabled)
            return "Enable a system-managed Windows pagefile, restart Windows, and test Siege once.";
        if (report.System.AvailableRamBytes < 2 * Gb)
            return "Restart Windows, launch Siege without other large applications, and test once while watching whether available RAM stays above 2 GB.";
        if (report.BattleEye.Failures.Count > 0 || !report.BattleEye.Installed && report.System.SiegeDetected)
            return "Use Siege's official BattlEye installer/repair option once, then launch the game and scan again.";
        if (report.SoftwareConflicts.Count > 0)
            return $"Exit {report.SoftwareConflicts[0]} completely, run Siege once, and see whether the crash repeats.";
        if (report.SiegeCrashes.Count >= 2)
            return "Use Steam or Ubisoft Connect to verify Siege's installed files once, then test the game again.";
        if (report.SiegeCrashes.Count == 0)
            return "Launch Siege and reproduce one crash, then run this scanner again so it can correlate the exact event time.";
        return "Test Siege once after a clean Windows restart, then scan again if it crashes so the newest event can be compared.";
    }
}

internal static class Format
{
    public static string Bytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    public static string Megabytes(long value) => value >= 1024 ? $"{value / 1024d:0.##} GB" : $"{value} MB";
}
