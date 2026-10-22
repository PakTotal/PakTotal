using CUE4Parse.UE4.Kismet;
using PakTotal.Sdk.Context;

namespace PakTotal.Sdk.Analysis;

public sealed record CallSite(BlueprintFunction Function, KismetExpression Statement, KismetExpression Call, FunctionRef Target)
{
    public KismetExpression[] Parameters => Call switch
    {
        EX_FinalFunction f => f.Parameters,
        EX_VirtualFunction v => v.Parameters,
        _ => []
    };

    public static IEnumerable<CallSite> FindAll(BlueprintFunction function)
    {
        foreach (var statement in function.Bytecode)
        {
            foreach (var expression in KismetTree.Descendants(statement))
            {
                if (FunctionRef.From(expression) is { } target) yield return new CallSite(function, statement, expression, target);
            }
        }
    }

    public static IEnumerable<CallSite> FindAll(ScanContext context) =>
        context.Blueprints.SelectMany(bp => bp.Functions).SelectMany(FindAll);

    public static IEnumerable<CallSite> Find(ScanContext context, string ownerName, string functionName) =>
        FindAll(context).Where(site => site.Target.Matches(ownerName, functionName));
}
