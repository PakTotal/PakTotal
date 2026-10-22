using System.Reflection;

namespace PakTotal.Core;

public static class CoreInfo
{
    public static string Cue4ParseVersion { get; } = typeof(CUE4Parse.FileProvider.IFileProvider).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
