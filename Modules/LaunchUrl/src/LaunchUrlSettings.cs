namespace PakTotal.Modules.LaunchUrl;

public sealed class LaunchUrlSettings
{
    public List<LaunchTarget> Targets { get; init; } = [];
    public List<string> NameReferences { get; init; } = [];
}

public sealed class LaunchTarget
{
    public required string Owner { get; init; }
    public required string Function { get; init; }
    public int UrlParameter { get; init; }
}
