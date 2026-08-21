using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace SiegeCrashScanner;

internal sealed record WheaDecodeResult(
    string Fingerprint,
    string Category,
    string Severity,
    bool IsPreviousError,
    string TechnicalDetails);

internal static class WheaDecoder
{
    private const int CperHeaderLength = 128;
    private const int SectionDescriptorLength = 72;

    private static readonly Dictionary<Guid, string> SectionTypes = new()
    {
        [new Guid("9876ccad-47b4-4bdb-b65e-16f193c4f3db")] = "Generic processor",
        [new Guid("dc3ea0b0-a144-4797-b95b-53fa242b6e1d")] = "x86/x64 processor",
        [new Guid("a5bc1114-6f64-4ede-b863-3e83ed7c83b1")] = "Memory / memory controller",
        [new Guid("d995e954-bbc1-430f-ad91-b44dcb3c6f35")] = "PCI Express",
        [new Guid("81212a96-09ed-4996-9471-8d729c8e69ed")] = "SoC / firmware error record"
    };

    public static WheaDecodeResult Decode(string eventXml, string message, DateTime time)
    {
        var rawText = FindRawData(eventXml);
        byte[]? bytes = null;
        if (!string.IsNullOrWhiteSpace(rawText))
        {
            try
            {
                var cleaned = new string(rawText.Where(Uri.IsHexDigit).ToArray());
                if (cleaned.Length >= 2 && cleaned.Length % 2 == 0) bytes = Convert.FromHexString(cleaned);
            }
            catch { }
        }

        if (bytes is null || bytes.Length < CperHeaderLength || Encoding.ASCII.GetString(bytes, 0, 4) != "CPER")
        {
            var fallback = SHA256.HashData(Encoding.UTF8.GetBytes($"{time.Ticks}|{message}"));
            return new WheaDecodeResult(Convert.ToHexString(fallback)[..12], CategorizeMessage(message),
                InferSeverity(message), false, "Windows did not expose a decodable CPER payload for this event.");
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(bytes))[..12];
        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2));
        var severityValue = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(104, 4));
        var previousError = (flags & 0x2) != 0;
        var sectionNames = new List<string>();

        var safeSectionCount = Math.Min(sectionCount, (ushort)32);
        for (var index = 0; index < safeSectionCount; index++)
        {
            var descriptor = CperHeaderLength + index * SectionDescriptorLength;
            if (descriptor + SectionDescriptorLength > bytes.Length) break;
            try
            {
                var type = new Guid(bytes.AsSpan(descriptor + 16, 16));
                sectionNames.Add(SectionTypes.TryGetValue(type, out var name) ? name : $"Vendor/unknown section {type}");
            }
            catch { }
        }

        sectionNames = sectionNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var category = ClassifySections(sectionNames, message);
        var severity = severityValue switch { 0 => "Recoverable", 1 => "Fatal", 2 => "Corrected", 3 => "Informational", _ => InferSeverity(message) };
        var details = $"CPER fingerprint {fingerprint}; {sectionCount} section(s)" +
                      (sectionNames.Count == 0 ? string.Empty : $": {string.Join(", ", sectionNames)}") + ". " +
                      (previousError
                          ? "Marked PreviousError, meaning firmware persisted the record from an earlier session; repeated log rows may be replays."
                          : "Not marked as a persisted previous-session record.");
        return new WheaDecodeResult(fingerprint, category, severity, previousError, details);
    }

    private static string? FindRawData(string eventXml)
    {
        try
        {
            var document = XDocument.Parse(eventXml);
            return document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName == "Data" &&
                string.Equals((string?)element.Attribute("Name"), "RawData", StringComparison.OrdinalIgnoreCase))?.Value;
        }
        catch { return null; }
    }

    private static string ClassifySections(List<string> sections, string message)
    {
        if (sections.Any(s => s.Contains("Memory", StringComparison.OrdinalIgnoreCase))) return "Memory / memory controller";
        if (sections.Any(s => s.Contains("PCI", StringComparison.OrdinalIgnoreCase))) return "PCIe";
        if (sections.Any(s => s.Contains("processor", StringComparison.OrdinalIgnoreCase))) return "CPU / processor";
        if (sections.Any(s => s.Contains("firmware", StringComparison.OrdinalIgnoreCase))) return "SoC / firmware";
        return CategorizeMessage(message);
    }

    private static string CategorizeMessage(string message)
    {
        if (message.Contains("memory", StringComparison.OrdinalIgnoreCase)) return "Memory / memory controller";
        if (message.Contains("PCI Express", StringComparison.OrdinalIgnoreCase) || message.Contains("PCIe", StringComparison.OrdinalIgnoreCase)) return "PCIe";
        if (message.Contains("processor", StringComparison.OrdinalIgnoreCase) || message.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return "CPU / processor";
        if (message.Contains("firmware", StringComparison.OrdinalIgnoreCase)) return "SoC / firmware";
        return "Hardware error";
    }

    private static string InferSeverity(string message)
    {
        if (message.Contains("fatal", StringComparison.OrdinalIgnoreCase) || message.Contains("uncorrected", StringComparison.OrdinalIgnoreCase)) return "Fatal";
        if (message.Contains("corrected", StringComparison.OrdinalIgnoreCase)) return "Corrected";
        return "Unknown";
    }
}
