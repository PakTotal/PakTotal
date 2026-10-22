namespace PakTotal.Sdk.Analysis;

public enum OpaqueKind
{
    Unknown,
    LocalPath
}

public sealed record StringSegment(string? Literal, OpaqueKind Kind = OpaqueKind.Unknown, string? Source = null)
{
    public bool IsLiteral => Literal is not null;

    public override string ToString() => Literal ?? $"<{(Source ?? Kind.ToString())}>";
}

public sealed class SymbolicString
{
    private SymbolicString(IReadOnlyList<StringSegment> segments) => Segments = segments;

    public IReadOnlyList<StringSegment> Segments { get; }

    public string KnownPrefix => string.Concat(Segments.TakeWhile(s => s.IsLiteral).Select(s => s.Literal));

    public StringSegment? FirstOpaque => Segments.FirstOrDefault(s => !s.IsLiteral);

    public static SymbolicString Opaque(OpaqueKind kind, string source) => new([new StringSegment(null, kind, source)]);

    public static object? Concat(object? left, object? right)
    {
        if (KismetValues.AsString(left) is { } a && KismetValues.AsString(right) is { } b) return LibraryArgs.Checked(a + b);
        var segments = new List<StringSegment>();
        Append(segments, left);
        Append(segments, right);
        return new SymbolicString(segments);
    }

    private static void Append(List<StringSegment> segments, object? value)
    {
        switch (value)
        {
            case SymbolicString symbolic:
                foreach (var segment in symbolic.Segments) Add(segments, segment);
                break;
            default:
                Add(segments, KismetValues.AsString(value) is { } literal ? new StringSegment(literal) : new StringSegment(null));
                break;
        }
    }

    private static void Add(List<StringSegment> segments, StringSegment segment)
    {
        if (segment.IsLiteral && segments.Count > 0 && segments[^1].IsLiteral)
            segments[^1] = new StringSegment(segments[^1].Literal + segment.Literal);
        else if (segments.Count < 256) segments.Add(segment);
    }

    public override string ToString() => string.Concat(Segments);
}
