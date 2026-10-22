using System.Diagnostics.CodeAnalysis;

namespace PakTotal.Sdk;

public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public static SemVer Parse(string value) =>
        TryParse(value, out var result) ? result : throw new FormatException($"Invalid semantic version '{value}'");

    public static bool TryParse([NotNullWhen(true)] string? value, out SemVer result)
    {
        result = default;
        var parts = value?.Split('-', '+')[0].Split('.');
        if (parts is not { Length: 3 }) return false;
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) || !int.TryParse(parts[2], out var patch))
            return false;
        result = new SemVer(major, minor, patch);
        return true;
    }

    public bool IsCompatibleWith(SemVer host) => Major == host.Major && Minor <= host.Minor;

    public int CompareTo(SemVer other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
