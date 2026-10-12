namespace Pyrop.Runtime;

public sealed record TypeConstructor(string Name, int Arity);

public abstract class PyropType : IEquatable<PyropType>
{
    public static PyropType Int { get; } = Primitive("int");

    public static PyropType Float { get; } = Primitive("float");

    public static PyropType String { get; } = Primitive("string");

    public static PyropType Bool { get; } = Primitive("bool");

    public static PyropType Void { get; } = Primitive("void");

    public abstract string Name { get; }

    public abstract bool IsConcrete { get; }

    public abstract bool Equals(PyropType? other);

    public abstract override int GetHashCode();

    public override bool Equals(object? obj) => obj is PyropType other && Equals(other);

    public override string ToString() => Name;

    public static bool operator ==(PyropType? left, PyropType? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(PyropType? left, PyropType? right) => !(left == right);

    public bool TryMatch(PyropType actual, Dictionary<TypeParameter, PyropType> bindings)
    {
        switch (this)
        {
            case TypeParameter parameter:
                if (bindings.TryGetValue(parameter, out var bound))
                {
                    return bound == actual;
                }

                bindings[parameter] = actual;
                return true;
            case ConstructedType pattern when actual is ConstructedType constructed && pattern.Constructor == constructed.Constructor:
                return pattern.Arguments.Zip(constructed.Arguments).All(pair => pair.First.TryMatch(pair.Second, bindings));
            default:
                return false;
        }
    }

    public PyropType Substitute(IReadOnlyDictionary<TypeParameter, PyropType> bindings) => this switch
    {
        TypeParameter parameter => bindings.GetValueOrDefault(parameter, parameter),
        ConstructedType { Arguments.Count: 0 } => this,
        ConstructedType constructed => new ConstructedType(
            constructed.Constructor,
            constructed.Arguments.Select(argument => argument.Substitute(bindings)).ToArray()),
        _ => throw new InvalidOperationException($"Unknown type {GetType().Name}."),
    };

    private static PyropType Primitive(string name) => new ConstructedType(new TypeConstructor(name, 0), []);
}

public sealed class ConstructedType : PyropType
{
    public ConstructedType(TypeConstructor constructor, IReadOnlyList<PyropType> arguments)
    {
        if (arguments.Count != constructor.Arity)
        {
            throw new ArgumentException(
                $"Type constructor '{constructor.Name}' takes {constructor.Arity} type arguments, not {arguments.Count}.",
                nameof(arguments));
        }

        Constructor = constructor;
        Arguments = arguments;
    }

    public TypeConstructor Constructor { get; }

    public IReadOnlyList<PyropType> Arguments { get; }

    public override string Name =>
        Arguments.Count == 0 ? Constructor.Name : $"{Constructor.Name} {string.Join(" ", Arguments.Select(argument => argument.Name))}";

    public override bool IsConcrete => Arguments.All(argument => argument.IsConcrete);

    public override bool Equals(PyropType? other) =>
        other is ConstructedType constructed
        && Constructor == constructed.Constructor
        && Arguments.SequenceEqual(constructed.Arguments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Constructor);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }
}

public sealed class TypeParameter(string name) : PyropType
{
    public override string Name { get; } = name;

    public override bool IsConcrete => false;

    public override bool Equals(PyropType? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
}

public static class PyropTypes
{
    public static IReadOnlyList<PyropType> ValueTypes { get; } =
        [PyropType.Int, PyropType.Float, PyropType.String, PyropType.Bool];

    public static IReadOnlyDictionary<string, TypeConstructor> ValueConstructors { get; } =
        ValueTypes.Cast<ConstructedType>().Select(type => type.Constructor).ToDictionary(constructor => constructor.Name);

    public static IEnumerable<string> Names => ValueConstructors.Keys.Append(PyropType.Void.Name);

    public static string DescribeValueTypes(string conjunction) =>
        $"{string.Join(", ", ValueTypes.SkipLast(1).Select(type => type.Name))} {conjunction} {ValueTypes[^1].Name}";
}
