using System.Text.RegularExpressions;

namespace PakTotal.Sdk.Analysis.FileWrites;

[Flags]
public enum FileWriteIssue
{
    None = 0,
    NullCharacter = 1,
    Escape = 2,
    Extension = 4,
    Unresolved = 8
}

public static partial class FilePathClassifier
{
    public static object? ValueOf(IReadOnlyList<object?> arguments, PathInput input)
    {
        var value = input.Parameter < arguments.Count ? arguments[input.Parameter] : UnknownValue.Instance;
        return input.Member is { } member && value is StructValue structValue ? structValue.Get(member) : value;
    }

    public static FileWriteIssue Classify(FileWriteTarget target, IReadOnlyList<object?> arguments)
    {
        var issues = FileWriteIssue.None;
        foreach (var (role, input) in target.Inputs())
        {
            var value = ValueOf(arguments, input);
            if (HasNull(value)) issues |= FileWriteIssue.NullCharacter;
            var isName = role == "fileName";
            if (isName ? NameEscapes(value) : PathEscapes(value, target.AllowedRoots)) issues |= FileWriteIssue.Escape;
            if (!(isName ? IsKnownName(value) : IsKnownPath(value, target.AllowedRoots))) issues |= FileWriteIssue.Unresolved;
        }

        if (target.AllowedExtensions is { } allowed && (target.FileName ?? target.Path) is { } subjectInput)
        {
            var subject = ValueOf(arguments, subjectInput);
            if (ExtensionOf(subject) is not { } extension) issues |= FileWriteIssue.Unresolved;
            else if (!IsEmpty(subject) && !allowed.Contains(extension, StringComparer.OrdinalIgnoreCase)) issues |= FileWriteIssue.Extension;
        }
        return issues;
    }

    public static bool HasNull(object? value) => Literals(value).Any(l => l.Contains('\0'));

    public static bool NameEscapes(object? name) => Literals(name).Any(l => l.IndexOfAny(['/', '\\', ':']) >= 0) || HasTraversal(name);

    public static bool PathEscapes(object? path, IReadOnlyCollection<string> allowedRoots)
    {
        if (HasTraversal(path)) return true;
        return path switch
        {
            string text => IsRooted(text),
            SymbolicString symbolic => symbolic.Segments.FirstOrDefault() switch
            {
                { Literal: { } head } => IsRooted(head),
                { Kind: OpaqueKind.LocalPath, Source: var source } => !IsAllowedRoot(source, allowedRoots),
                _ => false
            },
            _ => false
        };
    }

    public static bool IsKnownName(object? name) => name is string or null;

    public static bool IsKnownPath(object? path, IReadOnlyCollection<string> allowedRoots) => path switch
    {
        string or null => true,
        SymbolicString symbolic => symbolic.Segments.All(s => s.IsLiteral || s.Kind == OpaqueKind.LocalPath && IsAllowedRoot(s.Source, allowedRoots)),
        _ => false
    };

    public static string? ExtensionOf(object? path) => path switch
    {
        string text => System.IO.Path.GetExtension(text),
        SymbolicString symbolic => TrailingExtension(symbolic),
        _ => null
    };

    public static bool IsRooted(string text) => text.StartsWith('/') || text.StartsWith('\\') || DriveRoot().IsMatch(text);

    public static bool HasTraversal(object? value) => value switch
    {
        string text => Traversal().IsMatch(text),
        SymbolicString symbolic => Traversal().IsMatch(string.Concat(symbolic.Segments.Select(s => s.Literal ?? "/"))),
        _ => false
    };

    private static bool IsEmpty(object? value) => value is string text && string.IsNullOrWhiteSpace(text);

    private static bool IsAllowedRoot(string? source, IReadOnlyCollection<string> allowedRoots) =>
        source is not null && allowedRoots.Contains(source, StringComparer.OrdinalIgnoreCase);

    private static string? TrailingExtension(SymbolicString symbolic)
    {
        if (symbolic.Segments.Count == 0 || symbolic.Segments[^1].Literal is not { } tail) return null;
        var name = tail[(tail.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.Contains('.') ? System.IO.Path.GetExtension(name) : null;
    }

    private static IEnumerable<string> Literals(object? value) => value switch
    {
        string text => [text],
        SymbolicString symbolic => symbolic.Segments.Where(s => s.IsLiteral).Select(s => s.Literal!),
        _ => []
    };

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex DriveRoot();

    [GeneratedRegex(@"(^|[\\/])\.\.([\\/]|$)")]
    private static partial Regex Traversal();
}
