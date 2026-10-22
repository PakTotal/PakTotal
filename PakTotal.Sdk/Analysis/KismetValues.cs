using System.Globalization;

namespace PakTotal.Sdk.Analysis;

public sealed class UnknownValue
{
    public static readonly UnknownValue Instance = new();
    private UnknownValue() { }
    public override string ToString() => "<unknown>";
}

public sealed record ObjectRef(string Path)
{
    public override string ToString() => Path;
}

public sealed class StructValue(string structName, IReadOnlyList<object?> fields)
{
    public string StructName { get; } = structName;
    public IReadOnlyList<object?> Fields { get; } = fields;
    public Dictionary<string, object?> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

    public object? Get(string member) => Members.TryGetValue(member, out var value) ? value : Fields.Count == 1 ? Fields[0] : UnknownValue.Instance;

    public StructValue Copy()
    {
        var copy = new StructValue(StructName, Fields.Select(KismetValues.Copy).ToList());
        foreach (var (key, value) in Members) copy.Members[key] = KismetValues.Copy(value);
        return copy;
    }

    public override string ToString() => $"{StructName}({string.Join(", ", Members.Count > 0 ? Members.Values : Fields)})";
}

public sealed class SelfRef
{
    public static readonly SelfRef Instance = new();
    private SelfRef() { }
    public override string ToString() => "self";
}

public static class KismetValues
{
    public static bool IsUnknown(object? value) => value is UnknownValue;

    public static string Truncate(string value, int length = 300) => value.Length <= length ? value : value[..length] + "...";

    public static bool IsKnown(object? value) =>
        value is not (UnknownValue or SymbolicString) && (value is not List<object?> list || list.All(IsKnown));

    public static object? Copy(object? value) => value switch
    {
        List<object?> list => list.Select(Copy).ToList(),
        StructValue structValue => structValue.Copy(),
        _ => value
    };

    public static bool TryInt(object? value, out long result)
    {
        switch (value)
        {
            case byte b: result = b; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case bool flag: result = flag ? 1 : 0; return true;
            case float f: result = (long)f; return true;
            case double d: result = (long)d; return true;
            default: result = 0; return false;
        }
    }

    public static bool TryDouble(object? value, out double result)
    {
        switch (value)
        {
            case float f: result = f; return true;
            case double d: result = d; return true;
            default:
                var ok = TryInt(value, out var l);
                result = l;
                return ok;
        }
    }

    public static string? AsString(object? value) => value switch
    {
        string s => s,
        null => "",
        UnknownValue => null,
        SymbolicString => null,
        StructValue => null,
        List<object?> => null,
        bool b => b ? "true" : "false",
        float f => f.ToString("0.000000", CultureInfo.InvariantCulture),
        double d => d.ToString("0.000000", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    public static string Describe(object? value) => value switch
    {
        string s => $"\"{s}\"",
        List<object?> list => $"[{string.Join(", ", list.Take(16).Select(Describe))}{(list.Count > 16 ? ", ..." : "")}]",
        null => "None",
        SymbolicString symbolic => $"\"{symbolic}\"",
        _ => AsString(value) ?? value.ToString() ?? ""
    };
}
