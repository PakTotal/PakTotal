using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;

namespace PakTotal.Sdk.Context;

public sealed class BlueprintFunction
{
    private readonly Lazy<DecompiledGraph?> _graph;

    public BlueprintFunction(BlueprintAsset owner, UFunction function, bool isUbergraph, bool isEvent,
        Func<BlueprintFunction, DecompiledGraph?> decompile)
    {
        Owner = owner;
        Function = function;
        IsUbergraph = isUbergraph;
        IsEvent = isEvent;
        _graph = new Lazy<DecompiledGraph?>(() => decompile(this), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public BlueprintAsset Owner { get; }
    public UFunction Function { get; }
    public bool IsUbergraph { get; }
    public bool IsEvent { get; }
    public string Name => Function.Name;
    public KismetExpression[] Bytecode => Function.ScriptBytecode ?? [];

    public DecompiledGraph? GetGraph() => _graph.Value;

    public override string ToString() => $"{Owner.Name}.{Name}";
}
