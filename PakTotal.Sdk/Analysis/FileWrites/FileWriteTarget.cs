using PakTotal.Sdk.Modules;

namespace PakTotal.Sdk.Analysis.FileWrites;

public enum FileWriteRule
{
    Paths,
    AnyUse
}

public sealed class PathInput
{
    public int Parameter { get; init; }
    public string? Member { get; init; }
}

public sealed class FileWriteTarget
{
    public static readonly string[] DefaultAllowedRoots =
    [
        "ProjectSavedDir", "GetProjectSavedDirectory", "ProjectDir", "GetProjectDirectory", "ProjectContentDir",
        "GetProjectContentDirectory", "ProjectUserDir", "ProjectPersistentDownloadDir", "ProjectConfigDir", "ProjectLogDir",
        "ProjectIntermediateDir", "ProjectModsDir", "GameAgnosticSavedDir"
    ];

    public string Owner { get; init; } = "";
    public string Function { get; init; } = "";
    public string? DisplayName { get; init; }
    public FileWriteRule Rule { get; init; } = FileWriteRule.Paths;
    public PathInput? Path { get; init; }
    public PathInput? Directory { get; init; }
    public PathInput? FileName { get; init; }
    public string? BaseFolder { get; init; }
    public List<string> AllowedRoots { get; init; } = [.. DefaultAllowedRoots];
    public List<string>? AllowedExtensions { get; init; }

    public string Label => DisplayName ?? Function;

    public IEnumerable<(string Role, PathInput Input)> Inputs()
    {
        if (Path is not null) yield return ("path", Path);
        if (Directory is not null) yield return ("directory", Directory);
        if (FileName is not null) yield return ("fileName", FileName);
    }
}

public sealed class FindingRule
{
    public FindingLevel Level { get; init; }
    public Severity Severity { get; init; }
}

public sealed class FileWritePolicy
{
    public FindingRule NullCharacter { get; init; } = new() { Level = FindingLevel.Malicious, Severity = Severity.High };
    public FindingRule Escape { get; init; } = new() { Level = FindingLevel.Suspicious, Severity = Severity.Medium };
    public FindingRule Extension { get; init; } = new() { Level = FindingLevel.Malicious, Severity = Severity.Medium };
    public FindingRule Unresolved { get; init; } = new() { Level = FindingLevel.Suspicious, Severity = Severity.Medium };
    public FindingRule AnyUse { get; init; } = new() { Level = FindingLevel.Suspicious, Severity = Severity.Low };
}

public sealed class FileWriteSettings
{
    public List<FileWriteTarget> Targets { get; init; } = [];
    public FileWritePolicy Policy { get; init; } = new();
}
