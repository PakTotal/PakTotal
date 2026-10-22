using System.Text.RegularExpressions;
using CUE4Parse.UE4.Versions;

namespace PakTotal.Core.Engine;

public sealed record EngineVersionEntry(string Name, string Display, string? Engine);

public sealed partial class EngineVersionCatalog
{
    public EngineVersionCatalog()
    {
        var engines = new List<EngineVersionEntry>();
        var games = new List<EngineVersionEntry>();
        var engineNames = new Dictionary<uint, string>();

        foreach (var name in Enum.GetNames<EGame>().Where(n => !n.EndsWith("_LATEST", StringComparison.Ordinal)))
        {
            var match = EnginePattern().Match(name);
            if (!match.Success) continue;
            var value = (uint)Enum.Parse<EGame>(name);
            engineNames.TryAdd(value, name);
            engines.Add(new EngineVersionEntry(name, $"UE{match.Groups[1].Value}.{match.Groups[2].Value}", null));
        }

        foreach (var name in Enum.GetNames<EGame>().Where(n => !n.EndsWith("_LATEST", StringComparison.Ordinal)))
        {
            if (EnginePattern().IsMatch(name)) continue;
            var value = (uint)Enum.Parse<EGame>(name);
            if (engineNames.ContainsKey(value)) continue;
            var engine = engineNames.GetValueOrDefault(value & 0xFFFF0000);
            var display = name.StartsWith("GAME_", StringComparison.Ordinal) ? name[5..] : name;
            games.Add(new EngineVersionEntry(name, display, engine));
        }

        Engines = engines.OrderByDescending(e => (uint)Enum.Parse<EGame>(e.Name)).ToList();
        Games = games.OrderBy(g => g.Display, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<EngineVersionEntry> Engines { get; }
    public IReadOnlyList<EngineVersionEntry> Games { get; }

    public static bool TryParseGame(string? name, out EGame game)
    {
        game = default;
        return !string.IsNullOrWhiteSpace(name) && !name.All(char.IsDigit) && Enum.TryParse(name, true, out game) && Enum.IsDefined(game);
    }

    public static uint EngineOf(EGame game) => (uint)game & 0xFFFF0000;

    [GeneratedRegex(@"^GAME_UE(\d)_(\d+)$")]
    private static partial Regex EnginePattern();
}
