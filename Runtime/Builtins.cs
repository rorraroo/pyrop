namespace Pyrop.Runtime;

public interface IBuiltinInvocation
{
    Value Argument(int index);

    LineInput Input { get; }

    HostContext Host { get; }

    RandomSource Random { get; }

    IProgramOutput Output { get; }

    OutputStyle Style { get; }

    Exception Fail(string message);
}

public delegate Value? BuiltinImplementation(IBuiltinInvocation invocation);

public sealed record BuiltinOverload(IReadOnlyList<PyropType> Parameters, PyropType Result, BuiltinImplementation Implementation)
{
    public string DescribeParameters() =>
        Parameters.Count == 0 ? "no arguments" : string.Join(" ", Parameters.Select(type => type.Name));
}

public sealed record OverloadMatch(BuiltinOverload Overload, PyropType Result);

public sealed record BuiltinOperation(string Name, int Arity, IReadOnlyList<BuiltinOverload> Overloads)
{
    public bool ReturnsVoid => Overloads.All(overload => overload.Result == PyropType.Void);

    public OverloadMatch? FindOverload(IReadOnlyList<PyropType> argumentTypes)
    {
        foreach (var overload in Overloads)
        {
            var bindings = new Dictionary<TypeParameter, PyropType>();
            if (overload.Parameters.Count == argumentTypes.Count
                && overload.Parameters.Zip(argumentTypes).All(pair => pair.First.TryMatch(pair.Second, bindings)))
            {
                return new OverloadMatch(overload, overload.Result.Substitute(bindings));
            }
        }

        return null;
    }
}

public static class Builtins
{
    private static readonly PyropType I = PyropType.Int;
    private static readonly PyropType F = PyropType.Float;
    private static readonly PyropType S = PyropType.String;
    private static readonly PyropType B = PyropType.Bool;
    private static readonly PyropType V = PyropType.Void;

    public static IReadOnlyDictionary<string, BuiltinOperation> All { get; } = new BuiltinOperation[]
    {
        Operation("add",
            Overload([I, I], I, c => Int(c, () => checked(c.Argument(0).AsInt() + c.Argument(1).AsInt()))),
            Overload([F, F], F, c => Float(c, c.Argument(0).AsFloat() + c.Argument(1).AsFloat())),
            Overload([S, S], S, c => new StringValue(c.Argument(0).AsString() + c.Argument(1).AsString()))),
        Operation("subtract",
            Overload([I, I], I, c => Int(c, () => checked(c.Argument(0).AsInt() - c.Argument(1).AsInt()))),
            Overload([F, F], F, c => Float(c, c.Argument(0).AsFloat() - c.Argument(1).AsFloat()))),
        Operation("multiply",
            Overload([I, I], I, c => Int(c, () => checked(c.Argument(0).AsInt() * c.Argument(1).AsInt()))),
            Overload([F, F], F, c => Float(c, c.Argument(0).AsFloat() * c.Argument(1).AsFloat()))),
        Operation("divide",
            Overload([I, I], I, c => Int(c, () => checked(c.Argument(0).AsInt() / NonZero(c, c.Argument(1).AsInt())))),
            Overload([F, F], F, c => Float(c, c.Argument(0).AsFloat() / NonZero(c, c.Argument(1).AsFloat())))),
        Operation("modulo",
            Overload([I, I], I, c => new IntValue(Remainder(c.Argument(0).AsInt(), NonZero(c, c.Argument(1).AsInt()))))),
        Operation("equal",
            PyropTypes.ValueTypes
                .Select(type => Overload([type, type], B, c => BoolValue.Of(c.Argument(0) == c.Argument(1))))
                .ToArray()),
        Operation("greater", Comparisons(order => order > 0)),
        Operation("less", Comparisons(order => order < 0)),
        Operation("at_least", Comparisons(order => order >= 0)),
        Operation("at_most", Comparisons(order => order <= 0)),
        Operation("and",
            Overload([B, B], B, c => BoolValue.Of(c.Argument(0).AsBool() && c.Argument(1).AsBool()))),
        Operation("or",
            Overload([B, B], B, c => BoolValue.Of(c.Argument(0).AsBool() || c.Argument(1).AsBool()))),
        Operation("not",
            Overload([B], B, c => BoolValue.Of(!c.Argument(0).AsBool()))),
        Operation("read",
            Overload([], S, c => new StringValue(ReadLine(c, "read reached the end of input.")))),
        Operation("ask",
            Overload([S], S, c => new StringValue(Ask(c, c.Argument(0).AsString())))),
        Operation("confirm",
            Overload([S], B, c => BoolValue.Of(Confirm(c, c.Argument(0).AsString())))),
        Operation("input_ended",
            Overload([], B, c => BoolValue.Of(c.Input.HasEnded()))),
        Operation("argument_count",
            Overload([], I, c => new IntValue(c.Host.Arguments.Count))),
        Operation("argument_at",
            Overload([I], S, c => new StringValue(ArgumentAt(c, c.Argument(0).AsInt())))),
        Operation("read_environment",
            Overload([S], S, c => new StringValue(ReadEnvironment(c, c.Argument(0).AsString())))),
        Operation("read_file",
            Overload([S], S, c => new StringValue(TextFiles.Read(c, c.Argument(0).AsString())))),
        Operation("write_file",
            Overload([S, S], V, c => TextFiles.Write(c, c.Argument(0).AsString(), c.Argument(1).AsString(), append: false))),
        Operation("append_file",
            Overload([S, S], V, c => TextFiles.Write(c, c.Argument(0).AsString(), c.Argument(1).AsString(), append: true))),
        Operation("file_exists",
            Overload([S], B, c => BoolValue.Of(File.Exists(c.Argument(0).AsString())))),
        Operation("length_of",
            Overload([S], I, c => new IntValue(TextOperations.LengthOf(c.Argument(0).AsString())))),
        Operation("substring",
            Overload([S, I, I], S, c => new StringValue(
                TextOperations.Substring(c, c.Argument(0).AsString(), c.Argument(1).AsInt(), c.Argument(2).AsInt())))),
        Operation("char_at",
            Overload([S, I], S, c => new StringValue(TextOperations.CharAt(c, c.Argument(0).AsString(), c.Argument(1).AsInt())))),
        Operation("find",
            Overload([S, S], I, c => new IntValue(TextOperations.Find(c.Argument(0).AsString(), c.Argument(1).AsString())))),
        Operation("replace",
            Overload([S, S, S], S, c => new StringValue(
                TextOperations.Replace(c, c.Argument(0).AsString(), c.Argument(1).AsString(), c.Argument(2).AsString())))),
        Operation("starts_with",
            Overload([S, S], B, c => BoolValue.Of(TextOperations.StartsWith(c.Argument(0).AsString(), c.Argument(1).AsString())))),
        Operation("ends_with",
            Overload([S, S], B, c => BoolValue.Of(TextOperations.EndsWith(c.Argument(0).AsString(), c.Argument(1).AsString())))),
        Operation("upper",
            Overload([S], S, c => new StringValue(c.Argument(0).AsString().ToUpperInvariant()))),
        Operation("lower",
            Overload([S], S, c => new StringValue(c.Argument(0).AsString().ToLowerInvariant()))),
        Operation("trim",
            Overload([S], S, c => new StringValue(TextOperations.Trim(c.Argument(0).AsString())))),
        Operation("repeat",
            Overload([S, I], S, c => new StringValue(TextOperations.Repeat(c, c.Argument(0).AsString(), c.Argument(1).AsInt())))),
        Operation("pad_left",
            Overload([S, I, S], S, c => new StringValue(
                TextOperations.Pad(c, "pad_left", c.Argument(0).AsString(), c.Argument(1).AsInt(), c.Argument(2).AsString(), left: true)))),
        Operation("pad_right",
            Overload([S, I, S], S, c => new StringValue(
                TextOperations.Pad(c, "pad_right", c.Argument(0).AsString(), c.Argument(1).AsInt(), c.Argument(2).AsString(), left: false)))),
        Operation("abs",
            Overload([I], I, c => Int(c, () => Math.Abs(c.Argument(0).AsInt()))),
            Overload([F], F, c => new FloatValue(Math.Abs(c.Argument(0).AsFloat())))),
        Operation("negate",
            Overload([I], I, c => Int(c, () => checked(-c.Argument(0).AsInt()))),
            Overload([F], F, c => new FloatValue(-c.Argument(0).AsFloat()))),
        Operation("smaller", Choices(smaller: true)),
        Operation("larger", Choices(smaller: false)),
        Operation("to_power",
            Overload([I, I], I, c => Power(c, c.Argument(0).AsInt(), c.Argument(1).AsInt())),
            Overload([F, F], F, c => Float(c, Math.Pow(c.Argument(0).AsFloat(), c.Argument(1).AsFloat())))),
        Operation("square_root",
            Overload([F], F, c => new FloatValue(Math.Sqrt(NonNegative(c, c.Argument(0).AsFloat()))))),
        Operation("round",
            Overload([F], I, c => Conversions.FloatToInt(Math.Round(c.Argument(0).AsFloat(), MidpointRounding.AwayFromZero), c.Fail))),
        Operation("round_down",
            Overload([F], I, c => Conversions.FloatToInt(Math.Floor(c.Argument(0).AsFloat()), c.Fail))),
        Operation("random",
            Overload([F, F], F, c => new FloatValue(RandomBetween(c, c.Argument(0).AsFloat(), c.Argument(1).AsFloat())))),
        Operation("roll_dice",
            Overload([I, I], I, c => new IntValue(RollDice(c, c.Argument(0).AsInt(), c.Argument(1).AsInt())))),
        Operation("seed_random",
            Overload([I], V, c => SeedRandom(c, c.Argument(0).AsInt()))),
    }.ToDictionary(operation => operation.Name);

    private static BuiltinOverload[] Comparisons(Func<int, bool> holds) =>
    [
        Overload([I, I], B, c => BoolValue.Of(holds(c.Argument(0).AsInt().CompareTo(c.Argument(1).AsInt())))),
        Overload([F, F], B, c => BoolValue.Of(holds(c.Argument(0).AsFloat().CompareTo(c.Argument(1).AsFloat())))),
        Overload([S, S], B, c => BoolValue.Of(holds(string.CompareOrdinal(c.Argument(0).AsString(), c.Argument(1).AsString())))),
    ];

    private static BuiltinOverload[] Choices(bool smaller) =>
    [
        Overload([I, I], I, c => Choose(c, smaller, (first, second) => first.AsInt().CompareTo(second.AsInt()))),
        Overload([F, F], F, c => Choose(c, smaller, (first, second) => first.AsFloat().CompareTo(second.AsFloat()))),
        Overload([S, S], S, c => Choose(c, smaller, (first, second) => string.CompareOrdinal(first.AsString(), second.AsString()))),
    ];

    private static Value Choose(IBuiltinInvocation invocation, bool smaller, Func<Value, Value, int> compare)
    {
        var first = invocation.Argument(0);
        var second = invocation.Argument(1);
        var order = compare(first, second);
        return (smaller ? order <= 0 : order >= 0) ? first : second;
    }

    private static BuiltinOperation Operation(string name, params BuiltinOverload[] overloads) =>
        new(name, overloads[0].Parameters.Count, overloads);

    private static BuiltinOverload Overload(PyropType[] parameters, PyropType result, BuiltinImplementation implementation) =>
        new(parameters, result, implementation);

    private static IntValue Int(IBuiltinInvocation invocation, Func<long> compute)
    {
        try
        {
            return new IntValue(compute());
        }
        catch (OverflowException)
        {
            throw invocation.Fail("Integer overflow.");
        }
    }

    private static FloatValue Float(IBuiltinInvocation invocation, double result) => result switch
    {
        double.NaN => throw invocation.Fail("Float result is not a real number."),
        _ when double.IsFinite(result) => new FloatValue(result),
        _ => throw invocation.Fail("Float result is out of range."),
    };

    private static IntValue Power(IBuiltinInvocation invocation, long number, long exponent)
    {
        if (exponent < 0)
        {
            throw invocation.Fail($"'to_power' on ints cannot take a negative exponent ({exponent}); use floats instead.");
        }

        return Int(invocation, () =>
        {
            var result = 1L;
            while (true)
            {
                if ((exponent & 1) == 1)
                {
                    result = checked(result * number);
                }

                exponent >>= 1;
                if (exponent == 0)
                {
                    return result;
                }

                number = checked(number * number);
            }
        });
    }

    private static double NonNegative(IBuiltinInvocation invocation, double number) =>
        number >= 0 ? number : throw invocation.Fail($"Cannot take the square root of a negative number ({new FloatValue(number).ToText()}).");

    private static double RandomBetween(IBuiltinInvocation invocation, double lower, double upper)
    {
        if (!(lower < upper))
        {
            throw invocation.Fail(
                $"'random' needs a lower bound below its upper bound, found {new FloatValue(lower).ToText()} and {new FloatValue(upper).ToText()}.");
        }

        var fraction = invocation.Random.NextFraction();
        var result = lower * (1 - fraction) + upper * fraction;
        return result < upper ? Math.Max(result, lower) : Math.BitDecrement(upper);
    }

    private static long RollDice(IBuiltinInvocation invocation, long lower, long upper) =>
        lower <= upper
            ? invocation.Random.NextInRange(lower, upper)
            : throw invocation.Fail($"'roll_dice' needs a lower bound no greater than its upper bound, found {lower} and {upper}.");

    private static Value? SeedRandom(IBuiltinInvocation invocation, long seed)
    {
        invocation.Random.Seed(seed);
        return null;
    }

    private static string ReadLine(IBuiltinInvocation invocation, string endOfInputMessage)
    {
        var line = invocation.Input.ReadLine() ?? throw invocation.Fail(endOfInputMessage);
        invocation.Output.LineRead();
        return line;
    }

    private static string Ask(IBuiltinInvocation invocation, string prompt)
    {
        invocation.Output.WritePrompt(prompt, null, invocation.Style);
        return ReadLine(invocation, "'ask' reached the end of input.");
    }

    private static bool Confirm(IBuiltinInvocation invocation, string prompt)
    {
        while (true)
        {
            invocation.Output.WritePrompt(prompt.TrimEnd(), " [y/n] ", invocation.Style);
            var answer = ReadLine(invocation, "'confirm' reached the end of input.");
            switch (answer.Trim().ToLowerInvariant())
            {
                case "y" or "yes":
                    return true;
                case "n" or "no":
                    return false;
                default:
                    invocation.Output.Write("Please answer y or n.\n", OutputStyle.Warning);
                    break;
            }
        }
    }

    private static string ArgumentAt(IBuiltinInvocation invocation, long index)
    {
        var arguments = invocation.Host.Arguments;
        if (index < 0 || index >= arguments.Count)
        {
            var received = arguments.Count == 1 ? "1 argument" : $"{arguments.Count} arguments";
            throw invocation.Fail($"Argument index {index} is out of range; the program received {received}, numbered from 0.");
        }

        return arguments[(int)index];
    }

    private static string ReadEnvironment(IBuiltinInvocation invocation, string name)
    {
        if (name.Length == 0)
        {
            throw invocation.Fail("An environment variable name cannot be empty.");
        }

        if (name.Any(character => character == '=' || char.IsControl(character)))
        {
            throw invocation.Fail($"\"{name}\" is not a valid environment variable name; it cannot contain '=' or control characters.");
        }

        return invocation.Host.ReadEnvironment(name) ?? "";
    }

    private static long Remainder(long dividend, long divisor) => divisor == -1 ? 0 : dividend % divisor;

    private static long NonZero(IBuiltinInvocation invocation, long divisor) =>
        divisor != 0 ? divisor : throw invocation.Fail("Division by zero.");

    private static double NonZero(IBuiltinInvocation invocation, double divisor) =>
        divisor != 0 ? divisor : throw invocation.Fail("Division by zero.");
}
