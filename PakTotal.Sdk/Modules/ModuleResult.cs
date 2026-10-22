namespace PakTotal.Sdk.Modules;

public sealed class ModuleResult
{
    private readonly List<Finding> _findings = [];
    private readonly List<string> _coverageNotes = [];

    public IReadOnlyList<Finding> Findings => _findings;
    public IReadOnlyList<string> CoverageNotes => _coverageNotes;
    public Coverage Coverage { get; private set; } = Coverage.Complete;

    public ModuleResult Add(Finding finding)
    {
        _findings.Add(finding);
        return this;
    }

    public ModuleResult Notice(string code, string reason, string detail, string? assetPath = null) =>
        Add(new(FindingLevel.Notice, Severity.None, code, reason, detail, assetPath));

    public ModuleResult Suspicious(Severity severity, string code, string reason, string detail, string? assetPath = null) =>
        Add(new(FindingLevel.Suspicious, severity, code, reason, detail, assetPath));

    public ModuleResult Malicious(Severity severity, string code, string reason, string detail, string? assetPath = null) =>
        Add(new(FindingLevel.Malicious, severity, code, reason, detail, assetPath));

    public ModuleResult MarkPartial(string note)
    {
        if (Coverage == Coverage.Complete) Coverage = Coverage.Partial;
        _coverageNotes.Add(note);
        return this;
    }

    public ModuleResult MarkFailed(string note)
    {
        Coverage = Coverage.Failed;
        _coverageNotes.Add(note);
        return this;
    }

    public static ModuleResult Failed(string note) => new ModuleResult().MarkFailed(note);
}
