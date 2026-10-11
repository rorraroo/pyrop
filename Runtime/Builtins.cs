namespace Pyrop.Runtime;

public interface IBuiltinInvocation
{
    Value Argument(int index);

    TextReader Input { get; }

    Exception Fail(string message);
}

public delegate Value BuiltinImplementation(IBuiltinInvocation invocation);

public sealed record BuiltinOverload(IReadOnlyList<PyropType> Parameters, PyropType Result, BuiltinImplementation Implementation)
{
    public string DescribeParameters() =>
        Parameters.Count == 0 ? "no arguments" : string.Join(" ", Parameters.Select(type => type.Name()));
}

public sealed record BuiltinOperation(string Name, int Arity, IReadOnlyList<BuiltinOverload> Overloads)
{
    public BuiltinOverload? FindOverload(IReadOnlyList<PyropType> argumentTypes) =>
        Overloads.FirstOrDefault(overload => overload.Parameters.SequenceEqual(argumentTypes));
}

public static class Builtins
{
    private const PyropType I = PyropType.Int;
    private const PyropType F = PyropType.Float;
    private const PyropType S = PyropType.String;
    private const PyropType B = PyropType.Bool;

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
            Overload([I, I], I, c => Int(c, () => checked(c.Argument(0).AsInt() % NonZero(c, c.Argument(1).AsInt()))))),
        Operation("equal",
            PyropTypes.ValueTypes
                .Select(type => Overload([type, type], B, c => BoolValue.Of(c.Argument(0) == c.Argument(1))))
                .ToArray()),
        Operation("greater",
            Overload([I, I], B, c => BoolValue.Of(c.Argument(0).AsInt() > c.Argument(1).AsInt())),
            Overload([F, F], B, c => BoolValue.Of(c.Argument(0).AsFloat() > c.Argument(1).AsFloat())),
            Overload([S, S], B, c => BoolValue.Of(string.CompareOrdinal(c.Argument(0).AsString(), c.Argument(1).AsString()) > 0))),
        Operation("less",
            Overload([I, I], B, c => BoolValue.Of(c.Argument(0).AsInt() < c.Argument(1).AsInt())),
            Overload([F, F], B, c => BoolValue.Of(c.Argument(0).AsFloat() < c.Argument(1).AsFloat())),
            Overload([S, S], B, c => BoolValue.Of(string.CompareOrdinal(c.Argument(0).AsString(), c.Argument(1).AsString()) < 0))),
        Operation("and",
            Overload([B, B], B, c => BoolValue.Of(c.Argument(0).AsBool() && c.Argument(1).AsBool()))),
        Operation("or",
            Overload([B, B], B, c => BoolValue.Of(c.Argument(0).AsBool() || c.Argument(1).AsBool()))),
        Operation("not",
            Overload([B], B, c => BoolValue.Of(!c.Argument(0).AsBool()))),
        Operation("read",
            Overload([], S, c => new StringValue(c.Input.ReadLine() ?? throw c.Fail("read reached the end of input.")))),
    }.ToDictionary(operation => operation.Name);

    private static BuiltinOperation Operation(string name, params BuiltinOverload[] overloads) =>
        new(name, overloads[0].Parameters.Count, overloads);

    private static BuiltinOverload Overload(PyropType[] parameters, PyropType result, BuiltinImplementation implementation) =>
        new(parameters, result, implementation);

    private static IntValue Int(IBuiltinInvocation invocation, Func<int> compute)
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

    private static FloatValue Float(IBuiltinInvocation invocation, double result) =>
        double.IsFinite(result) ? new FloatValue(result) : throw invocation.Fail("Float result is out of range.");

    private static int NonZero(IBuiltinInvocation invocation, int divisor) =>
        divisor != 0 ? divisor : throw invocation.Fail("Division by zero.");

    private static double NonZero(IBuiltinInvocation invocation, double divisor) =>
        divisor != 0 ? divisor : throw invocation.Fail("Division by zero.");
}
