using PakTotal.Sdk.Analysis;
using PakTotal.Sdk.Modules;

namespace PakTotal.Modules.LaunchUrl;

public sealed record UrlClassification(FindingLevel Level, Severity Severity, string Code, string Reason);

public static class UrlClassifier
{
    private static readonly string[] WebSchemes = ["http://", "https://"];

    public static readonly UrlClassification Unresolved =
        new(FindingLevel.Suspicious, Severity.Unknown, "LaunchUrlUnresolved", "Calls LaunchURL with a target that could not be determined.");

    public static UrlClassification Classify(object? value) => value switch
    {
        string url => ClassifyPrefix(url, complete: true, opaque: null),
        SymbolicString symbolic => ClassifyPrefix(symbolic.KnownPrefix, complete: false, symbolic.FirstOpaque),
        _ => Unresolved
    };

    private static UrlClassification ClassifyPrefix(string prefix, bool complete, StringSegment? opaque)
    {
        var trimmed = prefix.TrimStart();
        if (trimmed.Length == 0)
        {
            if (complete) return new(FindingLevel.Suspicious, Severity.Low, "LaunchUrlEmpty", "Calls LaunchURL with an empty target.");
            return opaque?.Kind == OpaqueKind.LocalPath ? NonWeb : Unresolved;
        }

        if (WebSchemes.Any(s => trimmed.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
            return new(FindingLevel.Suspicious, Severity.Medium, "LaunchUrlWeb", "Opens a web page in the browser with LaunchURL.");
        if (!complete && WebSchemes.Any(s => s.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))) return Unresolved;
        return NonWeb;
    }

    private static readonly UrlClassification NonWeb =
        new(FindingLevel.Malicious, Severity.High, "LaunchUrlNonWeb", "Uses LaunchURL to open a local file or other non-web target.");
}
