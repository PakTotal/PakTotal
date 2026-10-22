using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Engine;
using UEBlueprintGraphViewer.Assets;

namespace PakTotal.Sdk.Context;

public sealed class BlueprintAsset
{
    public required string PackagePath { get; init; }
    public required IPackage Package { get; init; }
    public required UBlueprintGeneratedClass Class { get; init; }
    public UObject? DefaultObject { get; init; }
    public Asset? GraphAsset { get; init; }
    public string? GraphAssetError { get; init; }
    public List<BlueprintFunction> Functions { get; } = [];

    public string Name => Class.Name;

    public BlueprintFunction? FindFunction(string name) => Functions.Find(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => PackagePath;
}
