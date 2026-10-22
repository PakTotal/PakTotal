using CUE4Parse.UE4.Kismet;

namespace PakTotal.Sdk.Analysis;

public static class KismetTree
{
    public static IEnumerable<KismetExpression> Children(KismetExpression expression)
    {
        switch (expression)
        {
            case EX_FinalFunction call:
                return call.Parameters;
            case EX_VirtualFunction call:
                return call.Parameters;
            case EX_Context context:
                return [context.ObjectExpression, context.ContextExpression];
            case EX_InterfaceContext context:
                return [context.InterfaceValue];
            case EX_Let let:
                return [let.Variable, let.Assignment];
            case EX_LetBase let:
                return [let.Variable, let.Assignment];
            case EX_LetValueOnPersistentFrame let:
                return [let.AssignmentExpression];
            case EX_Return ret:
                return [ret.ReturnExpression];
            case EX_JumpIfNot jump:
                return [jump.BooleanExpression];
            case EX_Skip skip:
                return [skip.SkipExpression];
            case EX_PopExecutionFlowIfNot pop:
                return [pop.BooleanExpression];
            case EX_ComputedJump jump:
                return [jump.CodeOffsetExpression];
            case EX_Assert assert:
                return [assert.AssertExpression];
            case EX_ArrayConst array:
                return array.Elements;
            case EX_SetConst set:
                return set.Elements;
            case EX_MapConst map:
                return map.Elements;
            case EX_StructConst structConst:
                return structConst.Properties;
            case EX_SetArray setArray:
                return setArray.AssigningProperty is { } target ? [target, .. setArray.Elements] : setArray.Elements;
            case EX_SetSet setSet:
                return [setSet.SetProperty, .. setSet.Elements];
            case EX_SetMap setMap:
                return [setMap.MapProperty, .. setMap.Elements];
            case EX_Cast cast:
                return [cast.Target];
            case EX_CastBase cast:
                return [cast.Target];
            case EX_StructMemberContext member:
                return [member.StructExpression];
            case EX_ArrayGetByRef get:
                return [get.ArrayVariable, get.ArrayIndex];
            case EX_SwitchValue switchValue:
                return [
                    switchValue.IndexTerm,
                    .. switchValue.Cases.SelectMany(c => new[] { c.CaseIndexValueTerm, c.CaseTerm }),
                    switchValue.DefaultTerm
                ];
            case EX_BindDelegate bind:
                return [bind.Delegate, bind.ObjectTerm];
            case EX_AddMulticastDelegate add:
                return [add.Delegate, add.DelegateToAdd];
            case EX_RemoveMulticastDelegate remove:
                return [remove.Delegate, remove.DelegateToAdd];
            case EX_ClearMulticastDelegate clear:
                return [clear.DelegateToClear];
            case EX_CallMulticastDelegate call:
                return [call.Delegate, .. call.Parameters];
            case EX_TextConst text:
                return new[] { text.Value.SourceString, text.Value.KeyString, text.Value.Namespace, text.Value.TableIdString }
                    .OfType<KismetExpression>();
            case EX_SoftObjectConst soft:
                return [soft.Value];
            case EX_FieldPathConst fieldPath:
                return [fieldPath.Value];
            case EX_AutoRtfmTransact transact:
                return transact.Parameters;
            default:
                return [];
        }
    }

    public static IEnumerable<KismetExpression> Descendants(KismetExpression root)
    {
        var stack = new Stack<KismetExpression>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            foreach (var child in Children(current))
            {
                if (child is not null) stack.Push(child);
            }
        }
    }

    public static IEnumerable<KismetExpression> Descendants(IEnumerable<KismetExpression> statements) => statements.SelectMany(Descendants);

    public static IEnumerable<string> StringLiterals(KismetExpression root)
    {
        foreach (var expression in Descendants(root))
        {
            switch (expression)
            {
                case EX_StringConst s:
                    yield return s.Value;
                    break;
                case EX_UnicodeStringConst s:
                    yield return s.Value;
                    break;
                case EX_NameConst n:
                    yield return n.Value.Text;
                    break;
            }
        }
    }
}
