using CUE4Parse.UE4.Kismet;
using PakTotal.Sdk.Context;

namespace PakTotal.Sdk.Analysis;

public sealed record CollectedCall(CallSite Site, IReadOnlyList<IReadOnlyList<object?>> Observations)
{
    public IEnumerable<object?> ValuesOf(int parameter) =>
        Observations.Select(o => parameter < o.Count ? o[parameter] : UnknownValue.Instance);

    public object? StaticLiteral(int parameter)
    {
        var parameters = Site.Parameters;
        if (parameter >= parameters.Length) return UnknownValue.Instance;
        return parameters[parameter] switch
        {
            EX_StringConst s => s.Value,
            EX_UnicodeStringConst s => s.Value,
            _ => UnknownValue.Instance
        };
    }
}

public sealed record CollectionResult(IReadOnlyList<CollectedCall> Calls, IReadOnlyList<string> Notes);

public static class CallArgumentCollector
{
    private const int MaxObservationsPerSite = 64;

    public static CollectionResult Collect(ScanContext context, Func<FunctionRef, bool> match, CancellationToken token,
        InterpreterOptions? options = null)
    {
        var sites = context.Blueprints.SelectMany(b => b.Functions).SelectMany(CallSite.FindAll).Where(s => match(s.Target)).ToList();
        var notes = new List<string>();
        if (sites.Count == 0) return new CollectionResult([], notes);

        var observed = new Dictionary<KismetExpression, List<IReadOnlyList<object?>>>(ReferenceEqualityComparer.Instance);
        foreach (var blueprint in context.Blueprints)
        {
            token.ThrowIfCancellationRequested();
            var interpreter = new BytecodeInterpreter(blueprint, context, options);
            interpreter.CallObserved += observation =>
            {
                if (!match(observation.Target)) return;
                if (!observed.TryGetValue(observation.Call, out var list)) observed[observation.Call] = list = [];
                if (list.Count < MaxObservationsPerSite) list.Add(observation.Arguments);
            };
            interpreter.RunAllEntries();
            if (interpreter.BudgetExhausted) notes.Add($"{blueprint}: interpreter budget exhausted");
        }

        var calls = sites.Select(s => new CollectedCall(s, observed.GetValueOrDefault(s.Call) ?? [])).ToList();
        return new CollectionResult(calls, notes);
    }
}
