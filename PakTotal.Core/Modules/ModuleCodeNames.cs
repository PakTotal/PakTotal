using System.Security.Cryptography;
using System.Text;
using PakTotal.Sdk.Modules;

namespace PakTotal.Core.Modules;

public static class ModuleCodeNames
{
    private static readonly string[] Fallback =
    [
        "Badger", "Crane", "Ferret", "Heron", "Ibex", "Jackal", "Koala", "Lynx", "Marten", "Newt", "Ocelot", "Puffin",
        "Quail", "Raven", "Stoat", "Tapir", "Urchin", "Vole", "Walrus", "Yak", "Zebra", "Bison", "Gecko", "Moose"
    ];

    public static string For(ModuleManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.CodeName)) return manifest.CodeName.Trim();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(manifest.Id));
        return $"{Fallback[hash[0] % Fallback.Length]} {Convert.ToHexString(hash)[..2]}";
    }
}
