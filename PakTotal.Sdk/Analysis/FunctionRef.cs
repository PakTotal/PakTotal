using CUE4Parse.UE4.Kismet;

namespace PakTotal.Sdk.Analysis;

public sealed record FunctionRef(string Name, string? OwnerName, string? PackageName, bool IsExport, bool IsVirtual)
{
    public static FunctionRef? From(KismetExpression expression)
    {
        switch (expression)
        {
            case EX_FinalFunction call:
            {
                var resolved = call.StackNode.ResolvedObject;
                if (resolved is null) return new FunctionRef(call.StackNode.Name, null, null, call.StackNode.IsExport, false);
                var owner = resolved.Outer;
                return new FunctionRef(resolved.Name.Text, owner?.Name.Text, owner?.Outer?.Name.Text, call.StackNode.IsExport, false);
            }
            case EX_VirtualFunction call:
                return new FunctionRef(call.VirtualFunctionName.Text, null, null, false, true);
            default:
                return null;
        }
    }

    public bool Matches(string ownerName, string functionName)
    {
        if (!Name.Equals(functionName, StringComparison.OrdinalIgnoreCase)) return false;
        return OwnerName is null || OwnerName.Equals(ownerName, StringComparison.OrdinalIgnoreCase);
    }

    public string Key => OwnerName is null ? Name : $"{OwnerName}.{Name}";

    public override string ToString() => PackageName is null ? Key : $"{PackageName}.{Key}";
}
