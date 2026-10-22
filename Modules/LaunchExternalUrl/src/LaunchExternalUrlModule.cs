using PakTotal.Sdk.Analysis;
using PakTotal.Sdk.Context;
using PakTotal.Sdk.Modules;

namespace PakTotal.Modules.LaunchExternalUrl;

public sealed class LaunchExternalUrlSettings
{
    public string Owner { get; init; } = "KismetSystemLibrary";
    public string Function { get; init; } = "LaunchExternalUrl";
    public int DomainsParameter { get; init; }
    public int UrlParameter { get; init; } = 1;
    public string AllowedScheme { get; init; } = "http";
}

public sealed class LaunchExternalUrlModule : IScanModule
{
    private LaunchExternalUrlSettings _settings = new();
    private ExternalUrlFilter _filter = new("http");

    public void Initialize(ModuleManifest manifest)
    {
        _settings = manifest.GetSettings<LaunchExternalUrlSettings>() ?? new LaunchExternalUrlSettings();
        _filter = new ExternalUrlFilter(_settings.AllowedScheme);
    }

    public Task<ModuleResult> AnalyseAsync(ScanContext context, CancellationToken cancellationToken)
    {
        var result = new ModuleResult();
        var collection = CallArgumentCollector.Collect(context, f => f.Matches(_settings.Owner, _settings.Function), cancellationToken);
        foreach (var note in collection.Notes) result.MarkPartial(note);
        foreach (var call in collection.Calls) AnalyseCall(call, result);
        return Task.FromResult(result);
    }

    private void AnalyseCall(CollectedCall call, ModuleResult result)
    {
        var site = call.Site;
        var observations = call.Observations.Count > 0
            ? call.Observations.Select(o => (Url: At(o, _settings.UrlParameter), Domains: At(o, _settings.DomainsParameter))).ToList()
            : [(Url: call.StaticLiteral(_settings.UrlParameter), Domains: (object?)UnknownValue.Instance)];

        var location = $"{site.Function} @{site.Statement.StatementIndex}";
        var asset = site.Function.Owner.PackagePath;
        var outcomes = observations.Select(o => (o.Url, o.Domains, Outcome: _filter.Evaluate(o.Url, o.Domains))).ToList();

        if (outcomes.All(o => o.Outcome == FilterOutcome.Unknown)) result.MarkPartial($"{location}: target could not be reconstructed");
        foreach (var group in outcomes.GroupBy(o => o.Outcome))
        {
            var described = group.Select(o => $"{KismetValues.Describe(o.Url)} with {KismetValues.Describe(o.Domains)}");
            var shown = string.Join("; ", described.Distinct().Take(4));
            var detail = $"{_settings.Function} at {location}: {KismetValues.Truncate(shown, 600)}";
            switch (group.Key)
            {
                case FilterOutcome.Allowed:
                    result.Suspicious(Severity.Medium, "ExternalUrlAllowed", "Opens a web page in the browser with LaunchExternalUrl.", detail,
                        asset);
                    break;
                case FilterOutcome.Unknown:
                    result.Suspicious(Severity.Unknown, "ExternalUrlUnresolved",
                        "Calls LaunchExternalUrl with a target or domain list that could not be determined.", detail, asset);
                    break;
                case FilterOutcome.BlockedNonWeb:
                    result.Suspicious(Severity.Low, "ExternalUrlNonWeb",
                        "Calls LaunchExternalUrl with a non-web target, which the stock engine filter blocks.", detail, asset);
                    break;
                case FilterOutcome.BlockedByFilter:
                    result.Notice("ExternalUrlBlocked", "The URL is not in the allowed domains, so the engine blocks it.", detail, asset);
                    break;
                case FilterOutcome.BlockedWebScheme:
                    result.Notice("ExternalUrlScheme", "LaunchExternalUrl only allows http:// URLs, so the engine blocks this URL.", detail, asset);
                    break;
                case FilterOutcome.Empty:
                    result.Notice("ExternalUrlEmpty", "LaunchExternalUrl is called with an empty URL and does nothing.", detail, asset);
                    break;
            }
        }
    }

    private static object? At(IReadOnlyList<object?> arguments, int index) => index < arguments.Count ? arguments[index] : UnknownValue.Instance;
}
