namespace PakTotal.Sdk.Modules;

public sealed record Finding(FindingLevel Level, Severity Severity, string Code, string Reason, string Detail, string? AssetPath = null);
