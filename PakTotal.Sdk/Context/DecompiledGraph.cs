using UEBlueprintGraphViewer;

namespace PakTotal.Sdk.Context;

public sealed record DecompiledGraph(BPGraph Graph, IReadOnlyList<string> Problems, bool HasCriticalErrors)
{
    public bool IsComplete => Problems.Count == 0;
}
