using System.Text.Json;
using System.Text.Json.Serialization;

namespace PakTotal.Sdk.Modules;

public sealed class ModuleManifest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? CodeName { get; init; }
    public required string Version { get; init; }
    public required string SdkVersion { get; init; }
    public ModuleVisibility Visibility { get; init; } = ModuleVisibility.Public;
    public bool Enabled { get; init; } = true;
    public required string Assembly { get; init; }
    public required string EntryType { get; init; }
    public ModuleApplicability AppliesTo { get; init; } = new();
    public List<string> Requires { get; init; } = [];
    public JsonElement? Settings { get; init; }

    [JsonIgnore]
    public string Directory { get; set; } = "";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public T? GetSettings<T>() => Settings is { } settings ? settings.Deserialize<T>(JsonOptions) : default;
}

public enum ModuleVisibility
{
    Public,
    Private
}

public sealed class ModuleApplicability
{
    public string? MinEngine { get; init; }
    public string? MaxEngine { get; init; }
    public List<string> Games { get; init; } = [];
    public List<string> ExcludeGames { get; init; } = [];
    public List<string> RequiresAnyScriptPackage { get; init; } = [];
}
