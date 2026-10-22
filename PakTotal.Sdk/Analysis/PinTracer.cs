using UEBlueprintGraphViewer;
using UEBlueprintGraphViewer.Nodes;

namespace PakTotal.Sdk.Analysis;

public sealed record PinTrace(BPNode Node, GraphPin Pin, string? Literal, IReadOnlyList<PinTrace> Sources);

public static class PinTracer
{
    public static IEnumerable<BPNode> FindCallNodes(BPGraph graph, string ownerName, string functionName) =>
        graph.Nodes.OfType<K2Node_CallFunction>()
            .Where(n => n.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase))
            .Where(n => n.OuterName.Length == 0 || n.OuterName.EndsWith(ownerName, StringComparison.OrdinalIgnoreCase));

    public static string? GetCalledFunction(BPNode node) => node is K2Node_CallFunction call ? $"{call.OuterName}:{call.FunctionName}" : null;

    public static GraphPin? FindInput(BPNode node, string pinName) =>
        node.Input.Find(p => p.PinName.Equals(pinName, StringComparison.OrdinalIgnoreCase));

    public static PinTrace Trace(GraphPin input, int maxDepth = 32)
    {
        var visited = new HashSet<BPNode>();
        return TraceInput(input, maxDepth, visited);
    }

    private static PinTrace TraceInput(GraphPin input, int depth, HashSet<BPNode> visited)
    {
        var sources = new List<PinTrace>();
        if (depth > 0)
        {
            foreach (var linked in input.LinkedTo.Where(p => p.IsOutput))
            {
                var node = linked.ParentNode;
                if (!visited.Add(node)) continue;
                var upstream = node.Input.Where(p => p != node.ExecPin).Select(p => TraceInput(p, depth - 1, visited)).ToList();
                sources.Add(new PinTrace(node, linked, null, upstream));
            }
        }
        var literal = input.LinkedTo.Count == 0 ? input.Value : null;
        return new PinTrace(input.ParentNode, input, literal, sources);
    }

    public static IEnumerable<BPNode> UpstreamNodes(PinTrace trace)
    {
        foreach (var source in trace.Sources)
        {
            yield return source.Node;
            foreach (var node in UpstreamNodes(source)) yield return node;
        }
    }

    public static string DescribeCallInput(CallSite site, string ownerName, string functionName, int parameter)
    {
        var graph = site.Function.GetGraph();
        if (graph is null) return "";
        var nodes = FindCallNodes(graph.Graph, ownerName, functionName).ToList();
        var node = nodes.FirstOrDefault(n => ReferenceEquals(n.NodeInstr, site.Call)) ?? nodes.FirstOrDefault();
        var pin = node?.Input.Where(p => p != node.ExecPin).Skip(parameter).FirstOrDefault();
        return pin is null ? "" : $"\n{Describe(Trace(pin, 12))}";
    }

    public static string Describe(PinTrace trace, int indent = 0)
    {
        var pad = new string(' ', indent * 2);
        var head = trace.Literal is { Length: > 0 } literal
            ? $"{pad}{trace.Pin.PinName} = \"{literal}\""
            : $"{pad}{trace.Node.Name}.{trace.Pin.PinName}";
        return trace.Sources.Count == 0 ? head : head + "\n" + string.Join("\n", trace.Sources.Select(s => Describe(s, indent + 1)));
    }
}
