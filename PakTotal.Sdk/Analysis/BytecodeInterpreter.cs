using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Assets.Objects.Properties;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.Core.i18N;
using CUE4Parse.UE4.Objects.UObject;
using PakTotal.Sdk.Context;

namespace PakTotal.Sdk.Analysis;

public sealed class InterpreterOptions
{
    public int MaxStepsPerEntry { get; init; } = 100_000;
    public int MaxTotalSteps { get; init; } = 2_000_000;
    public int MaxCallDepth { get; init; } = 12;
    public int MaxForks { get; init; } = 64;
    public int MaxUnknownLoopIterations { get; init; } = 2;
}

public sealed record CallObservation(BlueprintFunction Function, KismetExpression Call, FunctionRef Target, IReadOnlyList<object?> Arguments);

public sealed class BytecodeInterpreter(BlueprintAsset blueprint, ScanContext? context = null, InterpreterOptions? options = null)
{
    private readonly InterpreterOptions _options = options ?? new InterpreterOptions();
    private readonly Dictionary<BlueprintFunction, Dictionary<int, int>> _offsets = [];
    private readonly HashSet<string> _problems = [];
    private int _entrySteps;
    private int _totalSteps;
    private int _forks;

    public event Action<CallObservation>? CallObserved;

    public IReadOnlyCollection<string> Problems => _problems;
    public bool BudgetExhausted { get; private set; }

    public void RunAllEntries()
    {
        var entries = blueprint.Functions.Where(f => !f.IsUbergraph).ToList();
        foreach (var entry in entries) Run(entry, NewState());

        var shared = NewState();
        foreach (var entry in entries) Run(entry, shared);
    }

    public void Run(BlueprintFunction entry) => Run(entry, NewState());

    private void Run(BlueprintFunction entry, State state)
    {
        if (_totalSteps >= _options.MaxTotalSteps)
        {
            BudgetExhausted = true;
            return;
        }
        _entrySteps = 0;
        _forks = 0;
        var frame = new Frame(entry, entry.IsUbergraph ? state.Persistent : NewLocals());
        try
        {
            ExecuteBody(frame, state, 0, 0);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _problems.Add($"{entry}: {e.GetType().Name}: {e.Message}");
        }
    }

    private State NewState() => State.CreateRoot(blueprint);

    private static Dictionary<string, object?> NewLocals() => new(StringComparer.OrdinalIgnoreCase);

    private bool Tick()
    {
        _entrySteps++;
        _totalSteps++;
        if (_entrySteps <= _options.MaxStepsPerEntry && _totalSteps <= _options.MaxTotalSteps) return true;
        BudgetExhausted = true;
        return false;
    }

    private void ExecuteBody(Frame frame, State state, int index, int depth)
    {
        var code = frame.Function.Bytecode;
        while (Tick())
        {
            if (index < 0 || index >= code.Length) return;
            switch (code[index])
            {
                case EX_JumpIfNot jump:
                {
                    var condition = AsBool(Eval(frame, state, jump.BooleanExpression, depth));
                    var target = IndexOf(frame.Function, jump.CodeOffset);
                    if (condition is null)
                    {
                        if (frame.CountUnknownBranch(index) > _options.MaxUnknownLoopIterations)
                        {
                            index = target > index ? target : index + 1;
                            break;
                        }
                        Fork(frame, state, target, depth);
                        index++;
                    }
                    else index = condition.Value ? index + 1 : target;
                    break;
                }
                case EX_Skip:
                    index++;
                    break;
                case EX_Jump jump:
                    index = IndexOf(frame.Function, jump.CodeOffset);
                    break;
                case EX_ComputedJump jump:
                {
                    if (!KismetValues.TryInt(Eval(frame, state, jump.CodeOffsetExpression, depth), out var offset))
                    {
                        _problems.Add($"{frame.Function}: computed jump with unknown offset");
                        return;
                    }
                    index = IndexOf(frame.Function, (uint)offset);
                    break;
                }
                case EX_PushExecutionFlow push:
                    frame.Flow.Push(push.PushingAddress);
                    index++;
                    break;
                case EX_PopExecutionFlow:
                    if (frame.Flow.Count == 0) return;
                    index = IndexOf(frame.Function, frame.Flow.Pop());
                    break;
                case EX_PopExecutionFlowIfNot pop:
                {
                    var condition = AsBool(Eval(frame, state, pop.BooleanExpression, depth));
                    if (condition is true)
                    {
                        index++;
                        break;
                    }
                    if (condition is null && frame.CountUnknownBranch(index) <= _options.MaxUnknownLoopIterations)
                    {
                        var forked = frame.Clone(state.CloneWorld(), out var forkedState);
                        if (forked.Flow.Count > 0 && CanFork()) ExecuteBody(forked, forkedState, IndexOf(frame.Function, forked.Flow.Pop()), depth);
                        index++;
                        break;
                    }
                    if (frame.Flow.Count == 0) return;
                    index = IndexOf(frame.Function, frame.Flow.Pop());
                    break;
                }
                case EX_Return ret:
                {
                    var returned = Eval(frame, state, ret.ReturnExpression, depth);
                    if (ret.ReturnExpression is not (EX_Nothing or null))
                    {
                        frame.ReturnValue = returned;
                        frame.HasReturnValue = true;
                    }
                    return;
                }
                case EX_EndOfScript:
                    return;
                default:
                    Exec(frame, state, code[index], depth);
                    index++;
                    break;
            }
        }
    }

    private bool CanFork()
    {
        if (_forks < _options.MaxForks)
        {
            _forks++;
            return true;
        }
        _problems.Add("fork budget exhausted");
        return false;
    }

    private void Fork(Frame frame, State state, int index, int depth)
    {
        if (!CanFork()) return;
        var forked = frame.Clone(state.CloneWorld(), out var forkedState);
        ExecuteBody(forked, forkedState, index, depth);
    }

    private int IndexOf(BlueprintFunction function, uint offset)
    {
        if (!_offsets.TryGetValue(function, out var map))
        {
            map = [];
            var code = function.Bytecode;
            for (var i = 0; i < code.Length; i++) map.TryAdd(code[i].StatementIndex, i);
            _offsets[function] = map;
        }
        if (map.TryGetValue((int)offset, out var index)) return index;
        _problems.Add($"{function}: jump to unknown offset {offset}");
        return -1;
    }

    private void Exec(Frame frame, State state, KismetExpression statement, int depth)
    {
        switch (statement)
        {
            case EX_Let let:
                Assign(frame, state, let.Variable, KismetValues.Copy(Eval(frame, state, let.Assignment, depth)), depth);
                break;
            case EX_LetBase let:
                Assign(frame, state, let.Variable, KismetValues.Copy(Eval(frame, state, let.Assignment, depth)), depth);
                break;
            case EX_LetValueOnPersistentFrame let:
                state.Persistent[let.DestinationProperty.ToString()] = KismetValues.Copy(Eval(frame, state, let.AssignmentExpression, depth));
                break;
            case EX_SetArray setArray:
            {
                var values = setArray.Elements.Select(e => KismetValues.Copy(Eval(frame, state, e, depth))).ToList();
                if (setArray.AssigningProperty is { } target) Assign(frame, state, target, values, depth);
                break;
            }
            default:
                Eval(frame, state, statement, depth);
                break;
        }
    }

    private object? Eval(Frame frame, State state, KismetExpression? expression, int depth)
    {
        switch (expression)
        {
            case null:
                return UnknownValue.Instance;
            case EX_StringConst s:
                return s.Value;
            case EX_UnicodeStringConst s:
                return s.Value;
            case EX_NameConst n:
                return n.Value.Text;
            case EX_TextConst t:
                if (t.Value.SourceString is { } source) return Eval(frame, state, source, depth);
                return t.Value.TableIdString is null ? "" : UnknownValue.Instance;
            case EX_IntConst i:
                return i.Value;
            case EX_IntConstByte i:
                return (int)i.Value;
            case EX_ByteConst b:
                return b.Value;
            case EX_Int64Const l:
                return l.Value;
            case EX_UInt64Const u:
                return unchecked((long)u.Value);
            case EX_IntZero:
                return 0;
            case EX_IntOne:
                return 1;
            case EX_True:
                return true;
            case EX_False:
                return false;
            case EX_FloatConst f:
                return (double)f.Value;
            case EX_DoubleConst d:
                return d.Value;
            case EX_SkipOffsetConst o:
                return (long)o.Value;
            case EX_Self:
                return SelfRef.Instance;
            case EX_NoObject or EX_NoInterface:
                return null;
            case EX_ObjectConst o:
                return new ObjectRef(o.Value.ResolvedObject?.GetPathName() ?? o.Value.Name);
            case EX_SoftObjectConst soft:
                return Eval(frame, state, soft.Value, depth);
            case EX_ArrayConst array:
                return array.Elements.Select(e => KismetValues.Copy(Eval(frame, state, e, depth))).ToList();
            case EX_StructConst structConst:
            {
                var fields = structConst.Properties.Select(p => KismetValues.Copy(Eval(frame, state, p, depth))).ToList();
                return new StructValue(structConst.Struct.Name, fields);
            }
            case EX_StructMemberContext member:
                return Eval(frame, state, member.StructExpression, depth) is StructValue owner
                    ? KismetValues.Copy(owner.Get(member.Property.ToString()))
                    : UnknownValue.Instance;
            case EX_LocalVariable or EX_LocalOutVariable:
                return Read(frame.Locals, ((EX_VariableBase)expression).Variable.ToString());
            case EX_InstanceVariable or EX_DefaultVariable:
                return ReadInstance(state, ((EX_VariableBase)expression).Variable.ToString());
            case EX_Context context:
                return EvalContext(frame, state, context, depth);
            case EX_InterfaceContext context:
                return Eval(frame, state, context.InterfaceValue, depth);
            case EX_FinalFunction or EX_VirtualFunction:
                return EvalCall(frame, state, expression, SelfRef.Instance, depth);
            case EX_Cast cast:
                return EvalCast(cast.ConversionType, Eval(frame, state, cast.Target, depth));
            case EX_CastBase cast:
                return Eval(frame, state, cast.Target, depth);
            case EX_ArrayGetByRef get:
            {
                var array = Eval(frame, state, get.ArrayVariable, depth);
                var hasIndex = KismetValues.TryInt(Eval(frame, state, get.ArrayIndex, depth), out var index);
                return array is List<object?> list && hasIndex && index >= 0 && index < list.Count ? list[(int)index] : UnknownValue.Instance;
            }
            case EX_SwitchValue switchValue:
                return EvalSwitch(frame, state, switchValue, depth);
            default:
                foreach (var child in KismetTree.Children(expression)) Eval(frame, state, child, depth);
                return UnknownValue.Instance;
        }
    }

    private object? EvalContext(Frame frame, State state, EX_Context context, int depth)
    {
        var target = Eval(frame, state, context.ObjectExpression, depth);
        if (target is SelfRef) return Eval(frame, state, context.ContextExpression, depth);
        if (FindBlueprint(target) is null && StaticClassPath(context.ObjectExpression) is { } classPath) target = new ObjectRef(classPath);
        if (context.ContextExpression is EX_FinalFunction or EX_VirtualFunction)
            return EvalCall(frame, state, context.ContextExpression, target, depth);
        if (context.ContextExpression is EX_InstanceVariable or EX_DefaultVariable &&
            ResolveVariableOwner((EX_VariableBase)context.ContextExpression, target) is { } owner)
            return ReadInstance(state.Foreign(owner), ((EX_VariableBase)context.ContextExpression).Variable.ToString());
        return UnknownValue.Instance;
    }

    private (BlueprintFunction? Callee, State State) ResolveCallee(State state, FunctionRef function, object? target)
    {
        if (target is SelfRef)
        {
            var own = function.IsExport || function.IsVirtual ? state.Blueprint.FindFunction(function.Name) : null;
            if (own is not null) return (own, state);
            return (FindBlueprint(function.OwnerName)?.FindFunction(function.Name), state);
        }

        var owner = FindBlueprint(function.OwnerName) ?? FindBlueprint(target);
        return owner?.FindFunction(function.Name) is { } callee ? (callee, state.Foreign(owner)) : (null, state);
    }

    private BlueprintAsset? ResolveVariableOwner(EX_VariableBase variable, object? target)
    {
        var pointer = variable.Variable;
        var ownerName = pointer.New?.ResolvedOwner?.ResolvedObject?.Name.Text ?? pointer.Old?.ResolvedObject?.Outer?.Name.Text;
        return FindBlueprint(ownerName) ?? FindBlueprint(target);
    }

    private BlueprintAsset? FindBlueprint(string? className) => context?.FindBlueprintByClass(className);

    private static string? StaticClassPath(KismetExpression expression)
    {
        if (expression is not EX_VariableBase { Variable.New: { } path } || path.Path.Length == 0) return null;
        try
        {
            var owner = path.ResolvedOwner?.Load<UStruct>();
            var property = owner?.ChildProperties?.OfType<FObjectProperty>().FirstOrDefault(p => p.Name.Text == path.Path[0].Text);
            return property?.PropertyClass?.ResolvedObject?.GetPathName();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private BlueprintAsset? FindBlueprint(object? target)
    {
        if (target is not ObjectRef reference) return null;
        var path = reference.Path;
        var defaultIndex = path.IndexOf("Default__", StringComparison.Ordinal);
        return defaultIndex >= 0 ? FindBlueprint(path[(defaultIndex + "Default__".Length)..]) : FindBlueprint(path);
    }

    private object? EvalSwitch(Frame frame, State state, EX_SwitchValue switchValue, int depth)
    {
        var index = Eval(frame, state, switchValue.IndexTerm, depth);
        if (KismetValues.IsUnknown(index)) return UnknownValue.Instance;
        foreach (var switchCase in switchValue.Cases)
        {
            if (ValuesEqual(index, Eval(frame, state, switchCase.CaseIndexValueTerm, depth)))
                return Eval(frame, state, switchCase.CaseTerm, depth);
        }
        return Eval(frame, state, switchValue.DefaultTerm, depth);
    }

    private static object? EvalCast(ECastToken conversion, object? value)
    {
        if (KismetValues.IsUnknown(value)) return value;
        return conversion switch
        {
            ECastToken.CST_ObjectToBool or ECastToken.CST_ObjectToBool2 or ECastToken.CST_InterfaceToBool or ECastToken.CST_InterfaceToBool2 =>
                value is not null,
            _ => value
        };
    }

    private object? EvalCall(Frame frame, State state, KismetExpression call, object? target, int depth)
    {
        var function = FunctionRef.From(call)!;
        var parameters = call switch
        {
            EX_FinalFunction f => f.Parameters,
            EX_VirtualFunction v => v.Parameters,
            _ => []
        };
        var args = parameters.Select(p => KismetValues.Copy(Eval(frame, state, p, depth))).ToArray();
        CallObserved?.Invoke(new CallObservation(frame.Function, call, function, args.Select(KismetValues.Copy).ToArray()));

        if (LibraryFunctions.TryGet(function, out var library))
        {
            try
            {
                var result = library.Invoke(args);
                foreach (var i in library.ByRef.Where(i => i < parameters.Length)) Assign(frame, state, parameters[i], args[i], depth);
                return result;
            }
            catch (UnknownArgumentException)
            {
                foreach (var i in library.ByRef.Where(i => i < parameters.Length)) Assign(frame, state, parameters[i], UnknownValue.Instance, depth);
                return UnknownValue.Instance;
            }
        }

        var (callee, calleeState) = ResolveCallee(state, function, target);
        if (callee is not null && depth < _options.MaxCallDepth) return Invoke(frame, state, calleeState, callee, parameters, args, depth + 1);

        var outputPrefix = $"CallFunc_{function.Name}_";
        foreach (var parameter in parameters)
        {
            if (parameter is EX_VariableBase variable && variable.Variable.ToString().StartsWith(outputPrefix, StringComparison.OrdinalIgnoreCase))
                Assign(frame, state, parameter, UnknownValue.Instance, depth);
        }
        return UnknownValue.Instance;
    }

    private object? Invoke(Frame caller, State callerState, State state, BlueprintFunction callee, KismetExpression[] parameters, object?[] args,
        int depth)
    {
        if (callee.IsUbergraph)
        {
            if (args.Length == 0 || !KismetValues.TryInt(args[0], out var entry)) return null;
            ExecuteBody(new Frame(callee, state.Persistent), state, IndexOf(callee, (uint)entry), depth);
            return null;
        }

        var signature = GetParameters(callee.Function);
        var inputs = signature.Where(p => !p.Flags.HasFlag(EPropertyFlags.ReturnParm)).ToList();
        var frame = new Frame(callee, NewLocals());
        for (var i = 0; i < inputs.Count; i++) frame.Locals[inputs[i].Name] = i < args.Length ? args[i] : UnknownValue.Instance;

        ExecuteBody(frame, state, 0, depth);

        for (var i = 0; i < inputs.Count && i < parameters.Length; i++)
        {
            var flags = inputs[i].Flags;
            if (flags.HasFlag(EPropertyFlags.OutParm) && !flags.HasFlag(EPropertyFlags.ConstParm))
                Assign(caller, callerState, parameters[i], Read(frame.Locals, inputs[i].Name), depth);
        }
        if (frame.HasReturnValue) return frame.ReturnValue;
        var returnParam = signature.FirstOrDefault(p => p.Flags.HasFlag(EPropertyFlags.ReturnParm));
        return returnParam.Name is null ? null : Read(frame.Locals, returnParam.Name);
    }

    private void Assign(Frame frame, State state, KismetExpression target, object? value, int depth)
    {
        switch (target)
        {
            case EX_LocalVariable or EX_LocalOutVariable:
                frame.Locals[((EX_VariableBase)target).Variable.ToString()] = value;
                break;
            case EX_InstanceVariable or EX_DefaultVariable:
                state.Instance[((EX_VariableBase)target).Variable.ToString()] = value;
                break;
            case EX_Context context:
            {
                var owner = Eval(frame, state, context.ObjectExpression, depth);
                if (owner is SelfRef) Assign(frame, state, context.ContextExpression, value, depth);
                else if (context.ContextExpression is EX_InstanceVariable or EX_DefaultVariable &&
                         ResolveVariableOwner((EX_VariableBase)context.ContextExpression, owner) is { } blueprintOwner)
                    state.Foreign(blueprintOwner).Instance[((EX_VariableBase)context.ContextExpression).Variable.ToString()] = value;
                break;
            }
            case EX_ArrayGetByRef get:
            {
                if (Eval(frame, state, get.ArrayVariable, depth) is not List<object?> list) break;
                if (!KismetValues.TryInt(Eval(frame, state, get.ArrayIndex, depth), out var index) || index < 0 || index >= list.Count) break;
                list[(int)index] = value;
                Assign(frame, state, get.ArrayVariable, list, depth);
                break;
            }
            case EX_StructMemberContext member:
            {
                var owner = Eval(frame, state, member.StructExpression, depth) as StructValue ?? new StructValue("", []);
                owner.Members[member.Property.ToString()] = value;
                Assign(frame, state, member.StructExpression, owner, depth);
                break;
            }
        }
    }

    private static object? Read(Dictionary<string, object?> locals, string name) =>
        locals.TryGetValue(name, out var value) ? value : UnknownValue.Instance;

    private object? ReadInstance(State state, string name)
    {
        if (state.Instance.TryGetValue(name, out var value)) return value;
        var tag = state.Blueprint.DefaultObject?.Properties.FirstOrDefault(p => p.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = tag?.Tag is { } property ? ConvertProperty(property) : ZeroValue(state.Blueprint, name);
        state.Instance[name] = value;
        return KismetValues.Copy(value);
    }

    private static object? ZeroValue(BlueprintAsset blueprint, string name)
    {
        var property = blueprint.Class.ChildProperties?.OfType<FProperty>()
            .FirstOrDefault(p => p.Name.Text.Equals(name, StringComparison.OrdinalIgnoreCase));
        return property switch
        {
            FStrProperty or FNameProperty or FTextProperty => "",
            FBoolProperty => false,
            FByteProperty => (byte)0,
            FIntProperty => 0,
            FFloatProperty or FDoubleProperty => 0d,
            FArrayProperty or FSetProperty => new List<object?>(),
            FObjectProperty or FInterfaceProperty => null,
            _ => UnknownValue.Instance
        };
    }

    public static object? ConvertProperty(FPropertyTagType property)
    {
        return property.GenericValue switch
        {
            string s => s,
            bool b => b,
            byte b => b,
            int i => i,
            long l => l,
            float f => (double)f,
            double d => d,
            FName n => n.Text,
            FText t => t.Text,
            UScriptArray array => array.Properties.Select(ConvertProperty).ToList(),
            FSoftObjectPath path => path.AssetPathName.Text,
            FPackageIndex index => index.IsNull ? null : new ObjectRef(index.ResolvedObject?.GetPathName() ?? index.Name),
            _ => UnknownValue.Instance
        };
    }

    private static bool? AsBool(object? value) => value switch
    {
        bool b => b,
        UnknownValue => null,
        _ when KismetValues.TryInt(value, out var v) => v != 0,
        _ => null
    };

    private static bool ValuesEqual(object? a, object? b)
    {
        if (KismetValues.TryDouble(a, out var x) && KismetValues.TryDouble(b, out var y)) return x == y;
        return string.Equals(KismetValues.AsString(a), KismetValues.AsString(b), StringComparison.OrdinalIgnoreCase);
    }

    private static List<(string Name, EPropertyFlags Flags)> GetParameters(UFunction function)
    {
        if (function.ChildProperties is { Length: > 0 } properties)
        {
            return properties.OfType<FProperty>()
                .Where(p => p.PropertyFlags.HasFlag(EPropertyFlags.Parm))
                .Select(p => (p.Name.Text, p.PropertyFlags))
                .ToList();
        }

        var result = new List<(string, EPropertyFlags)>();
        foreach (var child in function.Children ?? [])
        {
            if (child.TryLoad<UProperty>(out var property) && property.PropertyFlags.HasFlag(EPropertyFlags.Parm))
                result.Add((property.Name, property.PropertyFlags));
        }
        return result;
    }

    private sealed class World
    {
        public State Root = null!;
        public readonly Dictionary<BlueprintAsset, State> Others = [];
    }

    private sealed class State
    {
        private State(BlueprintAsset blueprint, World world, Dictionary<string, object?> instance, Dictionary<string, object?> persistent)
        {
            Blueprint = blueprint;
            World = world;
            Instance = instance;
            Persistent = persistent;
        }

        public BlueprintAsset Blueprint { get; }
        public World World { get; }
        public Dictionary<string, object?> Instance { get; }
        public Dictionary<string, object?> Persistent { get; }

        public static State CreateRoot(BlueprintAsset blueprint)
        {
            var world = new World();
            world.Root = new State(blueprint, world, NewLocals(), NewLocals());
            return world.Root;
        }

        public State Foreign(BlueprintAsset blueprint)
        {
            if (!World.Others.TryGetValue(blueprint, out var state))
                World.Others[blueprint] = state = new State(blueprint, World, NewLocals(), NewLocals());
            return state;
        }

        public State CloneWorld()
        {
            var world = new World();
            world.Root = World.Root.CopyInto(world);
            foreach (var (blueprint, other) in World.Others) world.Others[blueprint] = other.CopyInto(world);
            return ReferenceEquals(this, World.Root) ? world.Root : world.Others[Blueprint];
        }

        private State CopyInto(World world) => new(Blueprint, world, CopyLocals(Instance), CopyLocals(Persistent));
    }

    private sealed class Frame(BlueprintFunction function, Dictionary<string, object?> locals)
    {
        public BlueprintFunction Function { get; } = function;
        public Dictionary<string, object?> Locals { get; } = locals;
        public Stack<uint> Flow { get; private init; } = new();
        public object? ReturnValue { get; set; }
        public bool HasReturnValue { get; set; }
        private Dictionary<int, int> UnknownBranches { get; init; } = [];

        public int CountUnknownBranch(int index) => UnknownBranches[index] = UnknownBranches.GetValueOrDefault(index) + 1;

        public Frame Clone(State newState, out State state)
        {
            state = newState;
            var locals = Function.IsUbergraph ? newState.Persistent : CopyLocals(Locals);
            return new Frame(Function, locals)
            {
                Flow = new Stack<uint>(Flow.Reverse()),
                UnknownBranches = new Dictionary<int, int>(UnknownBranches)
            };
        }
    }

    private static Dictionary<string, object?> CopyLocals(Dictionary<string, object?> source)
    {
        var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in source) copy[key] = KismetValues.Copy(value);
        return copy;
    }
}
