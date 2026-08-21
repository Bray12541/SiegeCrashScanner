namespace SiegeCrashScanner;

public enum FindingStatus { Pass, Warning, Fail, Info }

public sealed class SystemSnapshot
{
    public string Cpu { get; set; } = "Detecting…";
    public string Gpu { get; set; } = "Detecting…";
    public string GpuDriverVersion { get; set; } = "Unknown";
    public string GpuDriverDate { get; set; } = "Unknown";
    public ulong TotalRamBytes { get; set; }
    public ulong AvailableRamBytes { get; set; }
    public ulong CommitUsedBytes { get; set; }
    public ulong CommitLimitBytes { get; set; }
    public string RamSpeed { get; set; } = "Unknown";
    public string WindowsVersion { get; set; } = "Detecting…";
    public string Motherboard { get; set; } = "Unknown";
    public string BiosVersion { get; set; } = "Unknown";
    public bool PagefileEnabled { get; set; }
    public long PagefileSizeMb { get; set; }
    public string SiegeInstallDirectory { get; set; } = string.Empty;
    public long SiegeDriveFreeBytes { get; set; } = -1;
    public List<string> CollectionNotes { get; } = [];

    public ulong UsedRamBytes => TotalRamBytes > AvailableRamBytes ? TotalRamBytes - AvailableRamBytes : 0;
    public bool SiegeDetected => !string.IsNullOrWhiteSpace(SiegeInstallDirectory);
}

public sealed class CrashEvent
{
    public DateTime Time { get; set; }
    public string Application { get; set; } = "Unknown";
    public string FaultingModule { get; set; } = "Unknown";
    public string ExceptionCode { get; set; } = "Unknown";
    public string FaultOffset { get; set; } = "Unknown";
    public string Explanation { get; set; } = string.Empty;
}

public sealed class DiagnosticEvent
{
    public DateTime Time { get; set; }
    public int EventId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class MemoryDiagnosticResult
{
    public DateTime? TestDate { get; set; }
    public bool? Passed { get; set; }
    public string Detail { get; set; } = "No Windows Memory Diagnostic result was found.";
}

public sealed class BattleEyeResult
{
    public bool Installed { get; set; }
    public string State { get; set; } = "Missing";
    public List<DiagnosticEvent> Failures { get; } = [];
}

public sealed class CorrelatedGpuEvent
{
    public required DiagnosticEvent DriverEvent { get; init; }
    public required CrashEvent Crash { get; init; }
    public double SecondsApart { get; init; }
}

public sealed class ScanReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public required SystemSnapshot System { get; init; }
    public List<CrashEvent> SiegeCrashes { get; } = [];
    public MemoryDiagnosticResult MemoryDiagnostic { get; set; } = new();
    public List<DiagnosticEvent> WheaEvents { get; } = [];
    public List<DiagnosticEvent> GpuEvents { get; } = [];
    public List<CorrelatedGpuEvent> CorrelatedGpuEvents { get; } = [];
    public BattleEyeResult BattleEye { get; set; } = new();
    public List<string> SoftwareConflicts { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<LikelyCause> LikelyCauses { get; } = [];
    public string RecommendedNextStep { get; set; } = string.Empty;
    public List<string> ScanNotes { get; } = [];
}

public sealed class Finding
{
    public required string Title { get; init; }
    public required FindingStatus Status { get; init; }
    public required string Detail { get; init; }
}

public sealed class LikelyCause
{
    public required string Name { get; init; }
    public required string Likelihood { get; init; }
    public required string Evidence { get; init; }
    public int RankScore { get; init; }
}
