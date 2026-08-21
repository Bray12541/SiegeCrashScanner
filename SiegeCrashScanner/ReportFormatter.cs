using System.Text;
using System.Text.RegularExpressions;

namespace SiegeCrashScanner;

internal static partial class ReportFormatter
{
    public static string Create(ScanReport report)
    {
        var b = new StringBuilder();
        b.AppendLine("SIEGE CRASH SCANNER - DIAGNOSTIC REPORT");
        b.AppendLine("========================================");
        b.AppendLine($"Generated: {report.GeneratedAt:F}");
        b.AppendLine("Privacy: username, computer name, IP addresses, and serial numbers are not collected.");
        b.AppendLine();
        b.AppendLine("SYSTEM INFORMATION");
        b.AppendLine($"CPU: {report.System.Cpu}");
        b.AppendLine($"GPU: {report.System.Gpu}");
        b.AppendLine($"GPU driver: {report.System.GpuDriverVersion} ({report.System.GpuDriverDate})");
        b.AppendLine($"RAM: {Format.Bytes(report.System.TotalRamBytes)} total; {Format.Bytes(report.System.UsedRamBytes)} used; {Format.Bytes(report.System.AvailableRamBytes)} available; {report.System.RamSpeed}");
        b.AppendLine($"Windows: {report.System.WindowsVersion}");
        b.AppendLine($"Motherboard: {report.System.Motherboard}");
        b.AppendLine($"BIOS: {report.System.BiosVersion} ({report.System.BiosDate})");
        b.AppendLine($"Last boot: {(report.System.LastBootTime is null ? "Unknown" : report.System.LastBootTime.Value.ToString("F"))}");
        b.AppendLine("Physical drives:");
        if (report.System.StorageDevices.Count == 0) b.AppendLine("- No physical-drive status returned.");
        foreach (var drive in report.System.StorageDevices)
            b.AppendLine($"- {drive.Model} | Status: {drive.Status} | Media: {drive.MediaType} | Interface: {drive.InterfaceType}");
        b.AppendLine($"Siege: {(report.System.SiegeDetected ? report.System.SiegeInstallDirectory : "Not detected")}");
        b.AppendLine();
        b.AppendLine("SUMMARY");
        foreach (var finding in report.Findings) b.AppendLine($"[{finding.Status.ToString().ToUpperInvariant()}] {finding.Title}: {finding.Detail}");

        b.AppendLine();
        b.AppendLine("SIEGE CRASH EVENTS (NEWEST FIRST)");
        if (report.SiegeCrashes.Count == 0) b.AppendLine("None found in the last 30 days.");
        foreach (var crash in report.SiegeCrashes)
        {
            b.AppendLine($"- {crash.Time:F}");
            b.AppendLine($"  Application: {crash.Application}");
            b.AppendLine($"  Faulting module: {crash.FaultingModule}");
            b.AppendLine($"  Exception code: {crash.ExceptionCode} — {crash.Explanation}");
            b.AppendLine($"  Fault offset: {crash.FaultOffset}");
        }

        b.AppendLine();
        b.AppendLine("WINDOWS MEMORY DIAGNOSTIC");
        b.AppendLine($"Result: {report.MemoryDiagnostic.Passed switch { true => "PASSED", false => "ERRORS DETECTED", _ => "NO CLEAR RESULT" }}");
        b.AppendLine($"Last test: {(report.MemoryDiagnostic.TestDate is null ? "Not found" : report.MemoryDiagnostic.TestDate.Value.ToString("F"))}");
        b.AppendLine(report.MemoryDiagnostic.Detail);

        b.AppendLine();
        b.AppendLine("WHEA EVENTS");
        b.AppendLine($"Raw log occurrences: {report.RawWheaEventCount}");
        b.AppendLine($"Unique CPER records: {report.WheaEvents.Count}");
        if (report.WheaEvents.Count == 0) b.AppendLine("No recent WHEA hardware errors found.");
        foreach (var item in report.WheaEvents)
        {
            b.AppendLine($"- Newest: {item.Time:F} | First seen: {item.FirstSeen:F} | Event {item.EventId} | {item.Category} | Severity: {item.Severity}");
            b.AppendLine($"  Fingerprint: {item.Fingerprint} | Occurrences: {item.OccurrenceCount} | PreviousError: {(item.IsPreviousError ? "Yes" : "No")}");
            b.AppendLine($"  Decode: {item.TechnicalDetails}");
            b.AppendLine($"  Windows message: {item.Message}");
        }
        foreach (var item in report.CorrelatedWheaEvents.OrderBy(item => item.SecondsApart))
            b.AppendLine($"  CORRELATED: record {item.HardwareEvent.Fingerprint} was {item.SecondsApart:0} seconds from the Siege crash at {item.Crash.Time:F}");

        b.AppendLine();
        b.AppendLine("GPU DRIVER");
        b.AppendLine($"Model: {report.System.Gpu}");
        b.AppendLine($"Version: {report.System.GpuDriverVersion}");
        b.AppendLine($"Date: {report.System.GpuDriverDate}");
        if (report.GpuEvents.Count == 0) b.AppendLine("No recent graphics-driver errors found.");
        foreach (var item in report.GpuEvents) b.AppendLine($"- {item.Time:F} | {item.Provider} Event {item.EventId} | {item.Message}");
        foreach (var item in report.CorrelatedGpuEvents.OrderBy(e => e.SecondsApart))
            b.AppendLine($"  CORRELATED: {item.SecondsApart:0} seconds from Siege crash at {item.Crash.Time:F}");

        b.AppendLine();
        b.AppendLine("STORAGE AND SYSTEM STABILITY EVENTS");
        if (report.StorageEvents.Count == 0) b.AppendLine("No recent disk, controller, or filesystem errors found.");
        foreach (var item in report.StorageEvents) b.AppendLine($"- {item.Time:F} | {item.Provider} Event {item.EventId} | {item.Message}");
        if (report.StabilityEvents.Count == 0) b.AppendLine("No unexpected shutdown or Kernel-Power events found.");
        foreach (var item in report.StabilityEvents) b.AppendLine($"- {item.Time:F} | {item.Provider} Event {item.EventId} | {item.Message}");

        if (report.Comparison.PreviousScanTime is not null)
        {
            b.AppendLine();
            b.AppendLine("CURRENT-SESSION SCAN COMPARISON");
            b.AppendLine($"Previous scan: {report.Comparison.PreviousScanTime:F}");
            b.AppendLine($"New Siege crashes: {report.Comparison.NewSiegeCrashes}");
            b.AppendLine($"New WHEA occurrences: {report.Comparison.NewWheaOccurrences}");
            b.AppendLine($"New GPU events: {report.Comparison.NewGpuEvents}");
            b.AppendLine($"New storage events: {report.Comparison.NewStorageEvents}");
        }

        b.AppendLine();
        b.AppendLine("VIRTUAL MEMORY / PAGEFILE");
        b.AppendLine($"Pagefile enabled: {(report.System.PagefileEnabled ? "Yes" : "No")}");
        b.AppendLine($"Allocated pagefile: {Format.Megabytes(report.System.PagefileSizeMb)}");
        b.AppendLine($"Current commit: {Format.Bytes(report.System.CommitUsedBytes)} / {Format.Bytes(report.System.CommitLimitBytes)}");

        b.AppendLine();
        b.AppendLine("BATTLEYE");
        b.AppendLine($"Installed: {(report.BattleEye.Installed ? "Yes" : "No")}");
        b.AppendLine($"State: {report.BattleEye.State}");
        if (report.BattleEye.Failures.Count == 0) b.AppendLine("No recent BattlEye service failures found.");
        foreach (var item in report.BattleEye.Failures) b.AppendLine($"- {item.Time:F} | Event {item.EventId} | {item.Message}");

        b.AppendLine();
        b.AppendLine("POSSIBLE SOFTWARE CONFLICTS");
        if (report.SoftwareConflicts.Count == 0) b.AppendLine("None from the checked list are currently running.");
        foreach (var item in report.SoftwareConflicts) b.AppendLine("- " + item + " (presence is not proof of causation)");

        b.AppendLine();
        b.AppendLine("MOST LIKELY CAUSES");
        if (report.LikelyCauses.Count == 0) b.AppendLine("No specific cause could be ranked from the evidence found.");
        for (var index = 0; index < report.LikelyCauses.Count; index++)
        {
            var cause = report.LikelyCauses[index];
            b.AppendLine($"{index + 1}. {cause.Name} — {cause.Likelihood} likelihood");
            b.AppendLine("   Evidence: " + cause.Evidence);
        }

        b.AppendLine();
        b.AppendLine("RECOMMENDED NEXT STEP");
        b.AppendLine(report.RecommendedNextStep);
        b.AppendLine();
        b.AppendLine("NOTES");
        b.AppendLine("This report is diagnostic guidance, not proof of a specific failed component.");
        foreach (var note in report.ScanNotes) b.AppendLine("- " + note);
        return Sanitize(b.ToString());
    }

    private static string Sanitize(string text)
    {
        var username = Environment.UserName;
        var machine = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(username)) text = text.Replace(username, "[redacted-user]", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(machine)) text = text.Replace(machine, "[redacted-computer]", StringComparison.OrdinalIgnoreCase);
        text = UserPathRegex().Replace(text, @"C:\Users\[redacted-user]");
        return Ipv4Regex().Replace(text, "[redacted-ip]");
    }

    [GeneratedRegex(@"C:\\Users\\[^\\\s]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserPathRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b")]
    private static partial Regex Ipv4Regex();
}
