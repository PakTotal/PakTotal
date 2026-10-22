using PakTotal.Sdk.Analysis;

namespace PakTotal.Modules.LaunchExternalUrl;

public enum FilterOutcome
{
    Allowed,
    BlockedNonWeb,
    BlockedByFilter,
    BlockedWebScheme,
    Empty,
    Unknown
}

public sealed class ExternalUrlFilter(string allowedScheme)
{
    private const string Delimiter = "://";

    public FilterOutcome Evaluate(object? url, object? domains)
    {
        var domainList = domains is List<object?> list && list.All(KismetValues.IsKnown)
            ? list.Select(KismetValues.AsString).OfType<string>().ToList()
            : null;

        switch (url)
        {
            case string known:
                return EvaluateKnown(known, domainList);
            case SymbolicString symbolic:
                if (symbolic.KnownPrefix.Length == 0 && symbolic.FirstOpaque?.Kind == OpaqueKind.LocalPath) return FilterOutcome.BlockedNonWeb;
                return EvaluatePrefix(symbolic.KnownPrefix, domainList);
            default:
                return FilterOutcome.Unknown;
        }
    }

    private FilterOutcome EvaluateKnown(string url, List<string>? domains)
    {
        if (url.Length == 0) return FilterOutcome.Empty;
        var index = url.IndexOf(Delimiter, StringComparison.Ordinal);
        if (index < 0) return FilterOutcome.BlockedNonWeb;
        var scheme = url[..index];
        if (!scheme.Equals(allowedScheme, StringComparison.OrdinalIgnoreCase))
            return IsWebScheme(scheme) ? FilterOutcome.BlockedWebScheme : FilterOutcome.BlockedNonWeb;
        if (domains is null) return FilterOutcome.Unknown;
        if (domains.Count == 0) return FilterOutcome.Allowed;

        var domain = ExtractDomain(url[(index + Delimiter.Length)..]);
        return domains.Any(d => Matches(domain, d)) ? FilterOutcome.Allowed : FilterOutcome.BlockedByFilter;
    }

    private FilterOutcome EvaluatePrefix(string prefix, List<string>? domains)
    {
        var index = prefix.IndexOf(Delimiter, StringComparison.Ordinal);
        if (index >= 0)
        {
            var scheme = prefix[..index];
            if (!scheme.Equals(allowedScheme, StringComparison.OrdinalIgnoreCase))
                return IsWebScheme(scheme) ? FilterOutcome.BlockedWebScheme : FilterOutcome.BlockedNonWeb;
            return domains is { Count: 0 } ? FilterOutcome.Allowed : FilterOutcome.Unknown;
        }

        var expected = allowedScheme + Delimiter;
        var couldBeWeb = expected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        return couldBeWeb ? FilterOutcome.Unknown : FilterOutcome.BlockedNonWeb;
    }

    private static bool IsWebScheme(string scheme) =>
        scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

    private static string ExtractDomain(string rest)
    {
        if (rest.StartsWith('['))
        {
            var end = rest.IndexOf(']');
            return end >= 0 ? rest[..(end + 1)] : rest;
        }
        var stop = rest.IndexOfAny(['/', '?', ':', '#']);
        return stop >= 0 ? rest[..stop] : rest;
    }

    private static bool Matches(string domain, string entry)
    {
        if (entry.StartsWith('.')) return domain.EndsWith(entry, StringComparison.OrdinalIgnoreCase);
        return entry.Length > 0 && domain.Equals(entry, StringComparison.OrdinalIgnoreCase);
    }
}
