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
    public string BiosDate { get; set; } = "Unknown";
    public DateTime? LastBootTime { get; set; }
    public bool PagefileEnabled { get; set; }
    public long PagefileSizeMb { get; set; }
    public string SiegeInstallDirectory { get; set; } = string.Empty;
    public long SiegeDriveFreeBytes { get; set; } = -1;
    public List<string> CollectionNotes { get; } = [];
    public List<StorageDevice> StorageDevices { get; } = [];

    public ulong UsedRamBytes => TotalRamBytes > AvailableRamBytes ? TotalRamBytes - AvailableRamBytes : 0;
    public bool SiegeDetected => !string.IsNullOrWhiteSpace(SiegeInstallDirectory);
}

public sealed class StorageDevice
{
    public string Model { get; set; } = "Unknown drive";
    public string Status { get; set; } = "Unknown";
    public string MediaType { get; set; } = "Unknown";
    public string InterfaceType { get; set; } = "Unknown";
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
    public string Severity { get; set; } = "Unknown";
    public string Fingerprint { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; } = 1;
    public DateTime FirstSeen { get; set; }
    public bool IsPreviousError { get; set; }
    public string TechnicalDetails { get; set; } = string.Empty;
    public List<DateTime> OccurrenceTimes { get; set; } = [];
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

public sealed class CorrelatedWheaEvent
{
    public required DiagnosticEvent HardwareEvent { get; init; }
    public required CrashEvent Crash { get; init; }
    public double SecondsApart { get; init; }
}

public sealed class ScanComparison
{
    public DateTime? PreviousScanTime { get; set; }
    public int NewSiegeCrashes { get; set; }
    public int NewWheaOccurrences { get; set; }
    public int NewGpuEvents { get; set; }
    public int NewStorageEvents { get; set; }
}

public sealed class ScanReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public required SystemSnapshot System { get; init; }
    public List<CrashEvent> SiegeCrashes { get; } = [];
    public MemoryDiagnosticResult MemoryDiagnostic { get; set; } = new();
    public List<DiagnosticEvent> WheaEvents { get; } = [];
    public int RawWheaEventCount { get; set; }
    public List<DiagnosticEvent> GpuEvents { get; } = [];
    public List<DiagnosticEvent> StorageEvents { get; } = [];
    public List<DiagnosticEvent> StabilityEvents { get; } = [];
    public List<CorrelatedGpuEvent> CorrelatedGpuEvents { get; } = [];
    public List<CorrelatedWheaEvent> CorrelatedWheaEvents { get; } = [];
    public BattleEyeResult BattleEye { get; set; } = new();
    public List<string> SoftwareConflicts { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<LikelyCause> LikelyCauses { get; } = [];
    public string RecommendedNextStep { get; set; } = string.Empty;
    public List<string> ScanNotes { get; } = [];
    public ScanComparison Comparison { get; set; } = new();
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
