using System.Diagnostics.Eventing.Reader;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SiegeCrashScanner;

internal static partial class EventLogCollector
{
    private const long ThirtyDaysMs = 30L * 24 * 60 * 60 * 1000;
    private static readonly HashSet<int> ServiceFailureEventIds = [7000, 7001, 7009, 7011, 7022, 7023, 7024, 7031, 7034, 7043];
    private static readonly string[] SiegeTokens = ["RainbowSix.exe", "RainbowSix_Vulkan.exe", "RainbowSix_DX12.exe", "Rainbow Six Siege", "RainbowSix_BE.exe"];

    public static List<CrashEvent> ReadSiegeCrashes(List<string> notes)
    {
        const string path = "Application";
        var query = $"*[System[(EventID=1000 or EventID=1001) and TimeCreated[timediff(@SystemTime) <= {ThirtyDaysMs}]]]";
        try
        {
            var events = Read(path, query, 250);
            var result = new List<CrashEvent>();
            foreach (var item in events)
            {
                var combined = string.Join("\n", item.Values.Prepend(item.Message));
                if (!SiegeTokens.Any(token => combined.Contains(token, StringComparison.OrdinalIgnoreCase))) continue;

                var app = item.Values.FirstOrDefault(v => SiegeTokens.Any(t => v.Contains(t, StringComparison.OrdinalIgnoreCase)))
                          ?? MatchValue(item.Message, @"faulting application name\s*:\s*([^,\r\n]+)") ?? "Rainbow Six Siege";
                var module = item.Values.Count > 3 ? item.Values[3] : string.Empty;
                if (string.IsNullOrWhiteSpace(module)) module = MatchValue(item.Message, @"faulting module name\s*:\s*([^,\r\n]+)") ?? "Unknown";
                var code = item.Values.FirstOrDefault(v => HexCodeRegex().IsMatch(v));
                code = code is null ? MatchValue(item.Message, @"exception code\s*:\s*(0x[0-9a-f]+)") : HexCodeRegex().Match(code).Value;
                var offset = MatchValue(item.Message, @"fault offset\s*:\s*(0x[0-9a-f]+)");
                if (offset is null && item.Values.Count > 7) offset = item.Values[7];

                result.Add(new CrashEvent
                {
                    Time = item.Time,
                    Application = Path.GetFileName(app.Trim()),
                    FaultingModule = Path.GetFileName(module.Trim()),
                    ExceptionCode = NormalizeHex(code),
                    FaultOffset = NormalizeHex(offset),
                    Explanation = ExplainException(code)
                });
            }
            return result.OrderByDescending(e => e.Time).Take(50).ToList();
        }
        catch (Exception ex)
        {
            notes.Add("Application crash log could not be fully read: " + ex.Message);
            return [];
        }
    }

    public static MemoryDiagnosticResult ReadMemoryDiagnostic(List<string> notes)
    {
        var query = "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']]]";
        try
        {
            var item = Read("System", query, 1).FirstOrDefault();
            if (item is null) return new MemoryDiagnosticResult();
            var text = item.Message;
            var passed = text.Contains("no errors", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("no memory errors", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("completed successfully", StringComparison.OrdinalIgnoreCase);
            var failed = text.Contains("hardware problems", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("errors were detected", StringComparison.OrdinalIgnoreCase) ||
                         text.Contains("detected errors", StringComparison.OrdinalIgnoreCase);
            return new MemoryDiagnosticResult
            {
                TestDate = item.Time,
                Passed = failed ? false : passed ? true : null,
                Detail = failed ? "Windows Memory Diagnostic reported memory errors."
                    : passed ? "Windows Memory Diagnostic detected no errors."
                    : "A memory diagnostic result exists, but Windows did not provide a clear pass/fail message."
            };
        }
        catch (Exception ex)
        {
            notes.Add("Windows Memory Diagnostic log could not be read: " + ex.Message);
            return new MemoryDiagnosticResult { Detail = "Memory diagnostic results were unavailable." };
        }
    }

    public static List<DiagnosticEvent> ReadWheaEvents(List<string> notes)
    {
        var query = $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and TimeCreated[timediff(@SystemTime) <= {ThirtyDaysMs}]]]";
        try
        {
            return Read("System", query, 100).Select(item => new DiagnosticEvent
            {
                Time = item.Time,
                EventId = item.Id,
                Provider = item.Provider,
                Category = CategorizeWhea(item.Message),
                Message = TrimMessage(item.Message)
            }).OrderByDescending(e => e.Time).ToList();
        }
        catch (Exception ex)
        {
            notes.Add("WHEA hardware log could not be read: " + ex.Message);
            return [];
        }
    }

    public static List<DiagnosticEvent> ReadGpuEvents(List<string> notes)
    {
        var query = $"*[System[TimeCreated[timediff(@SystemTime) <= {ThirtyDaysMs}] and (Provider[@Name='Display'] or Provider[@Name='nvlddmkm'] or Provider[@Name='amdkmdag'] or Provider[@Name='amdwddmg'])]]";
        try
        {
            return Read("System", query, 100).Select(item => new DiagnosticEvent
            {
                Time = item.Time,
                EventId = item.Id,
                Provider = item.Provider,
                Category = "Graphics driver",
                Message = TrimMessage(item.Message)
            }).OrderByDescending(e => e.Time).ToList();
        }
        catch (Exception ex)
        {
            notes.Add("Graphics-driver event log could not be read: " + ex.Message);
            return [];
        }
    }

    public static List<DiagnosticEvent> ReadBattleEyeFailures(List<string> notes)
    {
        var query = $"*[System[Provider[@Name='Service Control Manager'] and TimeCreated[timediff(@SystemTime) <= {ThirtyDaysMs}]]]";
        try
        {
            return Read("System", query, 300)
                .Where(item => ServiceFailureEventIds.Contains(item.Id) &&
                               (item.Message.Contains("BattlEye", StringComparison.OrdinalIgnoreCase) || item.Message.Contains("BEService", StringComparison.OrdinalIgnoreCase)))
                .Select(item => new DiagnosticEvent
                {
                    Time = item.Time,
                    EventId = item.Id,
                    Provider = item.Provider,
                    Category = "BattlEye service",
                    Message = TrimMessage(item.Message)
                }).OrderByDescending(e => e.Time).Take(30).ToList();
        }
        catch (Exception ex)
        {
            notes.Add("BattlEye service events could not be read: " + ex.Message);
            return [];
        }
    }

    private static List<EventData> Read(string logPath, string xPath, int maximum)
    {
        var result = new List<EventData>();
        var query = new EventLogQuery(logPath, PathType.LogName, xPath) { ReverseDirection = true, TolerateQueryErrors = true };
        using var reader = new EventLogReader(query);
        while (result.Count < maximum)
        {
            using var record = reader.ReadEvent();
            if (record is null) break;
            string message;
            try { message = record.FormatDescription() ?? string.Empty; }
            catch { message = string.Empty; }
            var values = record.Properties.Select(p => p.Value?.ToString() ?? string.Empty).ToList();
            if (values.Count == 0)
            {
                try
                {
                    var doc = XDocument.Parse(record.ToXml());
                    values.AddRange(doc.Descendants().Where(e => e.Name.LocalName == "Data").Select(e => e.Value));
                }
                catch { }
            }
            result.Add(new EventData(record.TimeCreated?.ToLocalTime() ?? DateTime.MinValue, record.Id, record.ProviderName ?? "Unknown", message, values));
        }
        return result;
    }

    private static string CategorizeWhea(string message)
    {
        if (message.Contains("memory controller", StringComparison.OrdinalIgnoreCase) || message.Contains("memory", StringComparison.OrdinalIgnoreCase)) return "Memory / memory controller";
        if (message.Contains("PCI Express", StringComparison.OrdinalIgnoreCase) || message.Contains("PCIe", StringComparison.OrdinalIgnoreCase)) return "PCIe";
        if (message.Contains("processor", StringComparison.OrdinalIgnoreCase) || message.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return "CPU / processor";
        if (message.Contains("corrected", StringComparison.OrdinalIgnoreCase)) return "Corrected hardware error";
        return "Hardware error";
    }

    private static string ExplainException(string? code) => NormalizeHex(code) switch
    {
        "0xc0000005" => "Possible memory access violation. This does not by itself mean bad RAM.",
        "0xc0000374" => "Possible heap corruption. This does not by itself mean bad RAM.",
        "0xc0000409" => "Possible stack/security-related corruption. This does not by itself mean bad RAM.",
        "Unknown" => "Windows did not provide a recognized exception code.",
        _ => "Windows recorded an application exception; the code alone does not identify the physical cause."
    };

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        var match = HexCodeRegex().Match(value);
        return match.Success ? match.Value.ToLowerInvariant() : value.Trim();
    }

    private static string? MatchValue(string input, string pattern)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string TrimMessage(string value)
    {
        value = Regex.Replace(value, @"\s+", " ").Trim();
        return value.Length <= 700 ? value : value[..700] + "…";
    }

    [GeneratedRegex("0x[0-9a-fA-F]{8,16}")]
    private static partial Regex HexCodeRegex();

    private sealed record EventData(DateTime Time, int Id, string Provider, string Message, List<string> Values);
}
