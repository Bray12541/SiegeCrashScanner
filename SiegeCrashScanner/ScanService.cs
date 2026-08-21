namespace SiegeCrashScanner;

internal sealed class ScanService
{
    public async Task<SystemSnapshot> ReadSystemInfoAsync(CancellationToken token = default) => (await SystemCollector.CollectAsync(token)).Snapshot;

    public async Task<ScanReport> ScanAsync(ScanReport? previousReport = null, IProgress<string>? progress = null, CancellationToken token = default)
    {
        progress?.Report("Reading system hardware and memory…");
        var (system, battleEye) = await SystemCollector.CollectAsync(token);
        var report = new ScanReport { System = system, BattleEye = battleEye };
        report.ScanNotes.AddRange(system.CollectionNotes);

        progress?.Report("Searching recent Siege crash events…");
        report.SiegeCrashes.AddRange(await Task.Run(() => EventLogCollector.ReadSiegeCrashes(report.ScanNotes), token));
        progress?.Report("Checking Windows Memory Diagnostic…");
        report.MemoryDiagnostic = await Task.Run(() => EventLogCollector.ReadMemoryDiagnostic(report.ScanNotes), token);
        progress?.Report("Checking WHEA hardware events…");
        report.WheaEvents.AddRange(await Task.Run(() => EventLogCollector.ReadWheaEvents(report.ScanNotes), token));
        report.RawWheaEventCount = report.WheaEvents.Sum(item => item.OccurrenceCount);
        progress?.Report("Checking graphics-driver events…");
        report.GpuEvents.AddRange(await Task.Run(() => EventLogCollector.ReadGpuEvents(report.ScanNotes), token));
        progress?.Report("Checking storage and system-stability events…");
        report.StorageEvents.AddRange(await Task.Run(() => EventLogCollector.ReadStorageEvents(report.ScanNotes), token));
        report.StabilityEvents.AddRange(await Task.Run(() => EventLogCollector.ReadStabilityEvents(report.ScanNotes), token));
        progress?.Report("Checking BattlEye and background software…");
        report.BattleEye.Failures.AddRange(await Task.Run(() => EventLogCollector.ReadBattleEyeFailures(report.ScanNotes), token));
        report.SoftwareConflicts.AddRange(await Task.Run(SoftwareScanner.FindRunning, token));

        foreach (var driverEvent in report.GpuEvents)
        foreach (var crash in report.SiegeCrashes)
        {
            var difference = Math.Abs((driverEvent.Time - crash.Time).TotalSeconds);
            if (difference <= 120) report.CorrelatedGpuEvents.Add(new CorrelatedGpuEvent { DriverEvent = driverEvent, Crash = crash, SecondsApart = difference });
        }

        foreach (var hardwareEvent in report.WheaEvents)
        foreach (var crash in report.SiegeCrashes)
        {
            var difference = Math.Abs((hardwareEvent.Time - crash.Time).TotalSeconds);
            if (difference <= 300) report.CorrelatedWheaEvents.Add(new CorrelatedWheaEvent { HardwareEvent = hardwareEvent, Crash = crash, SecondsApart = difference });
        }

        if (previousReport is not null)
        {
            report.Comparison = new ScanComparison
            {
                PreviousScanTime = previousReport.GeneratedAt,
                NewSiegeCrashes = report.SiegeCrashes.Count(item => item.Time > previousReport.GeneratedAt),
                NewWheaOccurrences = report.WheaEvents.Sum(item => item.OccurrenceTimes.Count(time => time > previousReport.GeneratedAt)),
                NewGpuEvents = report.GpuEvents.Count(item => item.Time > previousReport.GeneratedAt),
                NewStorageEvents = report.StorageEvents.Count(item => item.Time > previousReport.GeneratedAt)
            };
        }

        progress?.Report("Ranking evidence and preparing results…");
        ReportAnalyzer.Analyze(report);
        return report;
    }
}
