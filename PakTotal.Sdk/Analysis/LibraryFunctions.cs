using System.Globalization;
using static PakTotal.Sdk.Analysis.LibraryArgs;

namespace PakTotal.Sdk.Analysis;

public sealed record LibraryFunction(Func<object?[], object?> Invoke, int[] ByRef);

public sealed class UnknownArgumentException : Exception;

public static class LibraryArgs
{
    public const int MaxLength = 1 << 20;

    public static object? Arg(object?[] args, int index) => index < args.Length ? args[index] : UnknownValue.Instance;

    public static long L(object?[] args, int index) => KismetValues.TryInt(Arg(args, index), out var v) ? v : throw new UnknownArgumentException();

    public static int I(object?[] args, int index) => unchecked((int)L(args, index));

    public static byte B(object?[] args, int index) => unchecked((byte)L(args, index));

    public static double D(object?[] args, int index) =>
        KismetValues.TryDouble(Arg(args, index), out var v) ? v : throw new UnknownArgumentException();

    public static bool Z(object?[] args, int index) => Arg(args, index) switch
    {
        bool b => b,
        var other when KismetValues.TryInt(other, out var v) => v != 0,
        _ => throw new UnknownArgumentException()
    };

    public static string S(object?[] args, int index) => KismetValues.AsString(Arg(args, index)) ?? throw new UnknownArgumentException();

    public static List<object?> A(object?[] args, int index) => Arg(args, index) switch
    {
        List<object?> list => list,
        null => [],
        _ => throw new UnknownArgumentException()
    };

    public static string Checked(string value) => value.Length <= MaxLength ? value : throw new UnknownArgumentException();

    public static StringComparison Case(object?[] args, int index, bool defaultIgnoreCase = true)
    {
        var ignore = KismetValues.TryInt(Arg(args, index), out var v) ? v != 0 : defaultIgnoreCase;
        return ignore ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}

public static class LibraryFunctions
{
    private const string Math = "KismetMathLibrary";
    private const string Str = "KismetStringLibrary";
    private const string Text = "KismetTextLibrary";
    private const string Arr = "KismetArrayLibrary";

    private static readonly Dictionary<string, LibraryFunction> Table = new(StringComparer.OrdinalIgnoreCase);

    static LibraryFunctions()
    {
        RegisterMath();
        RegisterStrings();
        RegisterArrays();
        RegisterRuntimePaths();
    }

    public static void Register(string owner, string name, Func<object?[], object?> invoke, params int[] byRef)
    {
        lock (Table) Table[$"{owner}.{name}"] = new LibraryFunction(invoke, byRef);
    }

    public static bool TryGet(FunctionRef function, out LibraryFunction libraryFunction)
    {
        libraryFunction = null!;
        if (function.OwnerName is null) return false;
        lock (Table) return Table.TryGetValue(function.Key, out libraryFunction!);
    }

    private static void RegisterMath()
    {
        Register(Math, "Add_IntInt", a => unchecked(I(a, 0) + I(a, 1)));
        Register(Math, "Subtract_IntInt", a => unchecked(I(a, 0) - I(a, 1)));
        Register(Math, "Multiply_IntInt", a => unchecked(I(a, 0) * I(a, 1)));
        Register(Math, "Divide_IntInt", a => I(a, 1) == 0 ? 0 : unchecked(I(a, 0) / I(a, 1)));
        Register(Math, "Percent_IntInt", a => I(a, 1) == 0 ? 0 : unchecked(I(a, 0) % I(a, 1)));
        Register(Math, "And_IntInt", a => I(a, 0) & I(a, 1));
        Register(Math, "Or_IntInt", a => I(a, 0) | I(a, 1));
        Register(Math, "Xor_IntInt", a => I(a, 0) ^ I(a, 1));
        Register(Math, "Not_Int", a => ~I(a, 0));
        Register(Math, "Abs_Int", a => System.Math.Abs(I(a, 0)));
        Register(Math, "Max", a => System.Math.Max(I(a, 0), I(a, 1)));
        Register(Math, "Min", a => System.Math.Min(I(a, 0), I(a, 1)));
        Register(Math, "Clamp", a => System.Math.Clamp(I(a, 0), I(a, 1), System.Math.Max(I(a, 1), I(a, 2))));
        Register(Math, "SignOfInteger", a => System.Math.Sign(I(a, 0)));

        Register(Math, "Add_Int64Int64", a => unchecked(L(a, 0) + L(a, 1)));
        Register(Math, "Subtract_Int64Int64", a => unchecked(L(a, 0) - L(a, 1)));
        Register(Math, "Multiply_Int64Int64", a => unchecked(L(a, 0) * L(a, 1)));
        Register(Math, "Divide_Int64Int64", a => L(a, 1) == 0 ? 0L : unchecked(L(a, 0) / L(a, 1)));
        Register(Math, "Percent_Int64Int64", a => L(a, 1) == 0 ? 0L : unchecked(L(a, 0) % L(a, 1)));
        Register(Math, "And_Int64Int64", a => L(a, 0) & L(a, 1));
        Register(Math, "Or_Int64Int64", a => L(a, 0) | L(a, 1));
        Register(Math, "Xor_Int64Int64", a => L(a, 0) ^ L(a, 1));
        Register(Math, "Not_Int64", a => ~L(a, 0));
        Register(Math, "Abs_Int64", a => System.Math.Abs(L(a, 0)));

        Register(Math, "Add_ByteByte", a => unchecked((byte)(B(a, 0) + B(a, 1))));
        Register(Math, "Subtract_ByteByte", a => unchecked((byte)(B(a, 0) - B(a, 1))));
        Register(Math, "Multiply_ByteByte", a => unchecked((byte)(B(a, 0) * B(a, 1))));
        Register(Math, "Divide_ByteByte", a => B(a, 1) == 0 ? (byte)0 : (byte)(B(a, 0) / B(a, 1)));
        Register(Math, "Percent_ByteByte", a => B(a, 1) == 0 ? (byte)0 : (byte)(B(a, 0) % B(a, 1)));
        Register(Math, "BMax", a => System.Math.Max(B(a, 0), B(a, 1)));
        Register(Math, "BMin", a => System.Math.Min(B(a, 0), B(a, 1)));

        Register(Math, "Add_DoubleDouble", a => D(a, 0) + D(a, 1));
        Register(Math, "Subtract_DoubleDouble", a => D(a, 0) - D(a, 1));
        Register(Math, "Multiply_DoubleDouble", a => D(a, 0) * D(a, 1));
        Register(Math, "Divide_DoubleDouble", a => D(a, 1) == 0 ? 0d : D(a, 0) / D(a, 1));
        Register(Math, "Multiply_IntFloat", a => I(a, 0) * D(a, 1));

        foreach (var type in new[] { "IntInt", "Int64Int64", "ByteByte", "DoubleDouble" })
        {
            Register(Math, $"EqualEqual_{type}", a => D(a, 0) == D(a, 1));
            Register(Math, $"NotEqual_{type}", a => D(a, 0) != D(a, 1));
            Register(Math, $"Less_{type}", a => D(a, 0) < D(a, 1));
            Register(Math, $"LessEqual_{type}", a => D(a, 0) <= D(a, 1));
            Register(Math, $"Greater_{type}", a => D(a, 0) > D(a, 1));
            Register(Math, $"GreaterEqual_{type}", a => D(a, 0) >= D(a, 1));
        }

        Register(Math, "InRange_IntInt", a => InRange(L(a, 0), L(a, 1), L(a, 2), Z(a, 3), Z(a, 4)));
        Register(Math, "InRange_Int64Int64", a => InRange(L(a, 0), L(a, 1), L(a, 2), Z(a, 3), Z(a, 4)));

        Register(Math, "EqualEqual_BoolBool", a => Z(a, 0) == Z(a, 1));
        Register(Math, "NotEqual_BoolBool", a => Z(a, 0) != Z(a, 1));
        Register(Math, "Not_PreBool", a => !Z(a, 0));
        Register(Math, "BooleanAND", a => Z(a, 0) && Z(a, 1));
        Register(Math, "BooleanOR", a => Z(a, 0) || Z(a, 1));
        Register(Math, "BooleanXOR", a => Z(a, 0) ^ Z(a, 1));
        Register(Math, "BooleanNAND", a => !(Z(a, 0) && Z(a, 1)));
        Register(Math, "BooleanNOR", a => !(Z(a, 0) || Z(a, 1)));

        Register(Math, "Conv_ByteToInt", a => (int)B(a, 0));
        Register(Math, "Conv_ByteToInt64", a => (long)B(a, 0));
        Register(Math, "Conv_ByteToDouble", a => (double)B(a, 0));
        Register(Math, "Conv_IntToByte", a => unchecked((byte)I(a, 0)));
        Register(Math, "Conv_IntToInt64", a => (long)I(a, 0));
        Register(Math, "Conv_IntToDouble", a => (double)I(a, 0));
        Register(Math, "Conv_IntToBool", a => I(a, 0) != 0);
        Register(Math, "Conv_Int64ToInt", a => unchecked((int)L(a, 0)));
        Register(Math, "Conv_Int64ToByte", a => unchecked((byte)L(a, 0)));
        Register(Math, "Conv_Int64ToDouble", a => (double)L(a, 0));
        Register(Math, "Conv_BoolToInt", a => Z(a, 0) ? 1 : 0);
        Register(Math, "Conv_BoolToByte", a => Z(a, 0) ? (byte)1 : (byte)0);
        Register(Math, "Conv_BoolToDouble", a => Z(a, 0) ? 1d : 0d);
        Register(Math, "Conv_DoubleToInt64", a => (long)D(a, 0));
        Register(Math, "FTrunc", a => (int)D(a, 0));
        Register(Math, "Round", a => (int)System.Math.Round(D(a, 0), MidpointRounding.AwayFromZero));
        Register(Math, "FFloor", a => (int)System.Math.Floor(D(a, 0)));
        Register(Math, "FCeil", a => (int)System.Math.Ceiling(D(a, 0)));

        Register(Math, "SelectString", a => Z(a, 2) ? S(a, 0) : S(a, 1));
        Register(Math, "SelectInt", a => Z(a, 2) ? I(a, 0) : I(a, 1));
    }

    private static bool InRange(long value, long min, long max, bool inclusiveMin, bool inclusiveMax) =>
        (inclusiveMin ? value >= min : value > min) && (inclusiveMax ? value <= max : value < max);

    private static void RegisterStrings()
    {
        Register(Str, "Concat_StrStr", a => SymbolicString.Concat(Arg(a, 0), Arg(a, 1)));
        Register(Str, "Conv_IntToString", a => I(a, 0).ToString(CultureInfo.InvariantCulture));
        Register(Str, "Conv_Int64ToString", a => L(a, 0).ToString(CultureInfo.InvariantCulture));
        Register(Str, "Conv_ByteToString", a => B(a, 0).ToString(CultureInfo.InvariantCulture));
        Register(Str, "Conv_BoolToString", a => Z(a, 0) ? "true" : "false");
        Register(Str, "Conv_DoubleToString", a => D(a, 0).ToString("0.000000", CultureInfo.InvariantCulture));
        Register(Str, "Conv_NameToString", a => S(a, 0));
        Register(Str, "Conv_StringToName", a => S(a, 0));
        Register(Str, "Conv_StringToInt", a => int.TryParse(S(a, 0).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0);
        Register(Str, "Conv_StringToInt64", a =>
            long.TryParse(S(a, 0).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L);
        Register(Str, "Conv_StringToDouble", a =>
            double.TryParse(S(a, 0).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0d);
        Register(Str, "BuildString_Int", a => Checked(S(a, 0) + S(a, 1) + I(a, 2).ToString(CultureInfo.InvariantCulture) + S(a, 3)));
        Register(Str, "BuildString_Name", a => Checked(S(a, 0) + S(a, 1) + S(a, 2) + S(a, 3)));
        Register(Str, "BuildString_Bool", a => Checked(S(a, 0) + S(a, 1) + (Z(a, 2) ? "true" : "false") + S(a, 3)));
        Register(Str, "EqualEqual_StrStr", a => string.Equals(S(a, 0), S(a, 1), StringComparison.Ordinal));
        Register(Str, "EqualEqual_StriStri", a => string.Equals(S(a, 0), S(a, 1), StringComparison.OrdinalIgnoreCase));
        Register(Str, "NotEqual_StrStr", a => !string.Equals(S(a, 0), S(a, 1), StringComparison.Ordinal));
        Register(Str, "NotEqual_StriStri", a => !string.Equals(S(a, 0), S(a, 1), StringComparison.OrdinalIgnoreCase));
        Register(Str, "Len", a => S(a, 0).Length);
        Register(Str, "IsEmpty", a => S(a, 0).Length == 0);
        Register(Str, "GetSubstring", a => Mid(S(a, 0), I(a, 1), I(a, 2)));
        Register(Str, "Mid", a => Mid(S(a, 0), I(a, 1), I(a, 2)));
        Register(Str, "Left", a => Mid(S(a, 0), 0, I(a, 1)));
        Register(Str, "LeftChop", a => Mid(S(a, 0), 0, S(a, 0).Length - I(a, 1)));
        Register(Str, "Right", a => Mid(S(a, 0), S(a, 0).Length - System.Math.Max(0, I(a, 1)), I(a, 1)));
        Register(Str, "RightChop", a => Mid(S(a, 0), I(a, 1), S(a, 0).Length));
        Register(Str, "FindSubstring", a => FindSubstring(S(a, 0), S(a, 1), Z(a, 2), Z(a, 3), I(a, 4)));
        Register(Str, "Contains", a => FindSubstring(S(a, 0), S(a, 1), Z(a, 2), Z(a, 3), -1) >= 0);
        Register(Str, "GetCharacterAsNumber", a => I(a, 1) >= 0 && I(a, 1) < S(a, 0).Length ? (int)S(a, 0)[I(a, 1)] : 0);
        Register(Str, "GetCharacterArrayFromString", a => S(a, 0).Select(c => (object?)c.ToString()).ToList());
        Register(Str, "ParseIntoArray", a => ParseIntoArray(S(a, 0), S(a, 1), Z(a, 2)));
        Register(Str, "JoinStringArray", a => Checked(string.Join(S(a, 1), A(a, 0).Select(StringOrThrow))));
        Register(Str, "ToUpper", a => S(a, 0).ToUpperInvariant());
        Register(Str, "ToLower", a => S(a, 0).ToLowerInvariant());
        Register(Str, "LeftPad", a => Checked(new string(' ', System.Math.Clamp(I(a, 1), 0, MaxLength)) + S(a, 0)));
        Register(Str, "RightPad", a => Checked(S(a, 0) + new string(' ', System.Math.Clamp(I(a, 1), 0, MaxLength))));
        Register(Str, "IsNumeric", a => double.TryParse(S(a, 0), NumberStyles.Float, CultureInfo.InvariantCulture, out _));
        Register(Str, "StartsWith", a => S(a, 0).StartsWith(S(a, 1), Case(a, 2)));
        Register(Str, "EndsWith", a => S(a, 0).EndsWith(S(a, 1), Case(a, 2)));
        Register(Str, "Trim", a => S(a, 0).TrimStart());
        Register(Str, "TrimTrailing", a => S(a, 0).TrimEnd());
        Register(Str, "Reverse", a => new string(S(a, 0).Reverse().ToArray()));
        Register(Str, "Replace", a => S(a, 1).Length == 0 ? S(a, 0) : Checked(S(a, 0).Replace(S(a, 1), S(a, 2), Case(a, 3))));
        Register(Str, "Split", Split, 2, 3);

        Register(Text, "Conv_StringToText", a => S(a, 0));
        Register(Text, "Conv_TextToString", a => S(a, 0));
        Register(Text, "Conv_NameToText", a => S(a, 0));
        Register(Text, "TextIsEmpty", a => S(a, 0).Length == 0);
    }

    private static string StringOrThrow(object? value) => KismetValues.AsString(value) ?? throw new UnknownArgumentException();

    private static string Mid(string source, int start, int count)
    {
        if (count <= 0) return "";
        var from = System.Math.Clamp(start, 0, source.Length);
        var end = (int)System.Math.Clamp((long)start + count, from, source.Length);
        return source[from..end];
    }

    private static int FindSubstring(string searchIn, string substring, bool useCase, bool fromEnd, int startPosition)
    {
        if (substring.Length == 0) return -1;
        var comparison = useCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (fromEnd)
        {
            var start = startPosition < 0 || startPosition >= searchIn.Length ? searchIn.Length - 1 : startPosition;
            return start < 0 ? -1 : searchIn.LastIndexOf(substring, start, comparison);
        }
        return searchIn.IndexOf(substring, System.Math.Clamp(startPosition, 0, searchIn.Length), comparison);
    }

    private static List<object?> ParseIntoArray(string source, string delimiter, bool cullEmpty)
    {
        var parts = delimiter.Length == 0 ? [source] : source.Split(delimiter);
        return parts.Where(p => !cullEmpty || p.Length > 0).Select(p => (object?)p).ToList();
    }

    private static object? Split(object?[] a)
    {
        var source = S(a, 0);
        var search = S(a, 1);
        var comparison = Case(a, 4);
        var fromEnd = KismetValues.TryInt(Arg(a, 5), out var dir) && dir != 0;
        var index = search.Length == 0 ? -1 : fromEnd ? source.LastIndexOf(search, comparison) : source.IndexOf(search, comparison);
        if (index < 0)
        {
            a[2] = source;
            a[3] = "";
            return false;
        }
        a[2] = source[..index];
        a[3] = source[(index + search.Length)..];
        return true;
    }

    private static void RegisterArrays()
    {
        Register(Arr, "Array_Length", a => A(a, 0).Count);
        Register(Arr, "Array_LastIndex", a => A(a, 0).Count - 1);
        Register(Arr, "Array_IsValidIndex", a => I(a, 1) >= 0 && I(a, 1) < A(a, 0).Count);
        Register(Arr, "Array_IsEmpty", a => A(a, 0).Count == 0);
        Register(Arr, "Array_IsNotEmpty", a => A(a, 0).Count > 0);
        Register(Arr, "Array_Contains", a => A(a, 0).Any(v => Equals(v, Arg(a, 1))));
        Register(Arr, "Array_Find", a => A(a, 0).FindIndex(v => Equals(v, Arg(a, 1))));
        Register(Arr, "Array_Identical", a => A(a, 0).SequenceEqual(A(a, 1)));
        Register(Arr, "Array_Get", a =>
        {
            var list = A(a, 0);
            var index = I(a, 1);
            a[2] = index >= 0 && index < list.Count ? KismetValues.Copy(list[index]) : UnknownValue.Instance;
            return null;
        }, 2);
        Register(Arr, "Array_Add", a => Mutate(a, list =>
        {
            list.Add(KismetValues.Copy(Arg(a, 1)));
            return list.Count - 1;
        }), 0);
        Register(Arr, "Array_AddUnique", a => Mutate(a, list =>
        {
            var item = Arg(a, 1);
            if (list.Any(v => Equals(v, item))) return -1;
            list.Add(KismetValues.Copy(item));
            return list.Count - 1;
        }), 0);
        Register(Arr, "Array_Append", a => Mutate(a, list =>
        {
            list.AddRange(A(a, 1).Select(KismetValues.Copy));
            return null;
        }), 0);
        Register(Arr, "Array_Insert", a => Mutate(a, list =>
        {
            var index = I(a, 2);
            if (index >= 0 && index <= list.Count) list.Insert(index, KismetValues.Copy(Arg(a, 1)));
            return null;
        }), 0);
        Register(Arr, "Array_Set", a => Mutate(a, list =>
        {
            var index = I(a, 1);
            if (index >= list.Count && index < MaxLength && Z(a, 3))
            {
                while (list.Count <= index) list.Add(null);
            }
            if (index >= 0 && index < list.Count) list[index] = KismetValues.Copy(Arg(a, 2));
            return null;
        }), 0);
        Register(Arr, "Array_Remove", a => Mutate(a, list =>
        {
            var index = I(a, 1);
            if (index >= 0 && index < list.Count) list.RemoveAt(index);
            return null;
        }), 0);
        Register(Arr, "Array_RemoveItem", a => Mutate(a, list => list.RemoveAll(v => Equals(v, Arg(a, 1))) > 0), 0);
        Register(Arr, "Array_Clear", a => Mutate(a, list =>
        {
            list.Clear();
            return null;
        }), 0);
        Register(Arr, "Array_Reverse", a => Mutate(a, list =>
        {
            list.Reverse();
            return null;
        }), 0);
        Register(Arr, "Array_Swap", a => Mutate(a, list =>
        {
            int i = I(a, 1), j = I(a, 2);
            if (i >= 0 && j >= 0 && i < list.Count && j < list.Count) (list[i], list[j]) = (list[j], list[i]);
            return null;
        }), 0);
        Register(Arr, "Array_Resize", a => Mutate(a, list =>
        {
            var size = System.Math.Clamp(I(a, 1), 0, MaxLength);
            if (list.Count > size) list.RemoveRange(size, list.Count - size);
            while (list.Count < size) list.Add(null);
            return null;
        }), 0);
    }

    private static void RegisterRuntimePaths()
    {
        const string system = "KismetSystemLibrary";
        const string paths = "BlueprintPathsLibrary";
        var systemPaths = new[]
        {
            "GetPlatformUserDir", "GetProjectDirectory", "GetProjectContentDirectory", "GetProjectSavedDirectory", "GetSystemPath"
        };
        foreach (var name in systemPaths)
            Register(system, name, _ => SymbolicString.Opaque(OpaqueKind.LocalPath, name));
        var literals = new[]
        {
            "MakeLiteralString", "MakeLiteralName", "MakeLiteralText", "MakeLiteralInt", "MakeLiteralInt64", "MakeLiteralByte", "MakeLiteralBool",
            "MakeLiteralFloat", "MakeLiteralDouble"
        };
        foreach (var name in literals)
            Register(system, name, a => KismetValues.Copy(Arg(a, 0)));
        Register(system, "ConvertToAbsolutePath", a => LocalPath(a, "ConvertToAbsolutePath"));
        Register(system, "NormalizeFilename", a => LocalPath(a, "NormalizeFilename"));

        foreach (var name in new[]
                 {
                     "LaunchDir", "EngineDir", "EngineUserDir", "EngineVersionAgnosticUserDir", "EngineContentDir", "EngineConfigDir",
                     "EngineIntermediateDir", "EngineSavedDir", "EnginePluginsDir", "RootDir", "ProjectDir", "ProjectUserDir", "ProjectContentDir",
                     "ProjectConfigDir", "ProjectSavedDir", "ProjectIntermediateDir", "ProjectPluginsDir", "ProjectModsDir",
                     "ProjectPersistentDownloadDir", "SourceConfigDir", "GeneratedConfigDir", "ScreenShotDir", "ProjectLogDir",
                     "GameAgnosticSavedDir", "GetProjectFilePath", "VideoCaptureDir", "ProfilingDir", "CloudDir"
                 })
            Register(paths, name, _ => SymbolicString.Opaque(OpaqueKind.LocalPath, name));
        Register(paths, "ConvertRelativePathToFull", a => LocalPath(a, "ConvertRelativePathToFull"));
    }

    private static object LocalPath(object?[] args, string source) =>
        KismetValues.AsString(Arg(args, 0)) is { } known ? known : SymbolicString.Opaque(OpaqueKind.LocalPath, source);

    private static object? Mutate(object?[] args, Func<List<object?>, object?> action)
    {
        var list = A(args, 0).Select(KismetValues.Copy).ToList();
        var result = action(list);
        args[0] = list;
        return result;
    }
}
