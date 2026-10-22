namespace PakTotal.Sdk.Config;

public enum IniOperation
{
    Set,
    Add,
    AddDuplicate,
    Remove,
    Clear
}

public sealed record IniEntry(IniOperation Operation, string Key, string Value, int Line);

public sealed record IniSection(string Name, int Line, IReadOnlyList<IniEntry> Entries)
{
    public IEnumerable<IniEntry> Find(string key) => Entries.Where(e => e.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
}

public sealed class IniDocument
{
    private IniDocument(IReadOnlyList<IniSection> sections) => Sections = sections;

    public IReadOnlyList<IniSection> Sections { get; }

    public static IniDocument Parse(string text)
    {
        var sections = new List<IniSection>();
        string? name = null;
        var nameLine = 0;
        var entries = new List<IniEntry>();
        var lines = text.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim().TrimEnd('\r');
            if (line.Length == 0 || line[0] is ';' or '#') continue;

            if (line[0] == '[' && line.EndsWith(']'))
            {
                if (name is not null) sections.Add(new IniSection(name, nameLine, entries));
                name = line[1..^1].Trim();
                nameLine = i + 1;
                entries = [];
                continue;
            }

            var equals = line.IndexOf('=');
            if (name is null || equals <= 0) continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            var operation = IniOperation.Set;
            if (key.Length > 1)
            {
                (operation, key) = key[0] switch
                {
                    '+' => (IniOperation.Add, key[1..]),
                    '.' => (IniOperation.AddDuplicate, key[1..]),
                    '-' => (IniOperation.Remove, key[1..]),
                    '!' => (IniOperation.Clear, key[1..]),
                    _ => (IniOperation.Set, key)
                };
            }
            entries.Add(new IniEntry(operation, key.Trim(), value, i + 1));
        }

        if (name is not null) sections.Add(new IniSection(name, nameLine, entries));
        return new IniDocument(sections);
    }
}
