using System.Text;

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
            case DeclaredType declared:
                return declared == actual;
            default:
                return false;
        }
    }

    public IEnumerable<TypeParameter> TypeParameters => this switch
    {
        TypeParameter parameter => [parameter],
        ConstructedType constructed => constructed.Arguments.SelectMany(argument => argument.TypeParameters),
        _ => [],
    };

    public IEnumerable<DeclaredType> DeclaredTypes => this switch
    {
        DeclaredType declared => [declared],
        ConstructedType constructed => constructed.Arguments.SelectMany(argument => argument.DeclaredTypes),
        _ => [],
    };

    public virtual string NameQualifying(IReadOnlySet<DeclaredType> ambiguous) => Name;

    public PyropType Substitute(IReadOnlyDictionary<TypeParameter, PyropType> bindings) => this switch
    {
        TypeParameter parameter => bindings.GetValueOrDefault(parameter, parameter),
        DeclaredType => this,
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

    public override string Name
    {
        get
        {
            if (Arguments.Count == 0)
            {
                return Constructor.Name;
            }

            return NameQualifying(NoAmbiguity);
        }
    }

    private static readonly IReadOnlySet<DeclaredType> NoAmbiguity = new HashSet<DeclaredType>();

    public override string NameQualifying(IReadOnlySet<DeclaredType> ambiguous)
    {
        if (Arguments.Count == 0)
        {
            return Constructor.Name;
        }

        var builder = new StringBuilder();
        AppendName(builder, ambiguous);
        return builder.ToString();
    }

    private void AppendName(StringBuilder builder, IReadOnlySet<DeclaredType> ambiguous)
    {
        builder.Append(Constructor.Name);
        foreach (var argument in Arguments)
        {
            builder.Append(' ');
            if (argument is ConstructedType constructed)
            {
                constructed.AppendName(builder, ambiguous);
            }
            else
            {
                builder.Append(argument.NameQualifying(ambiguous));
            }
        }
    }

    public override bool IsConcrete => Arguments.All(argument => argument.IsConcrete);

    public override bool Equals(PyropType? other) =>
        ReferenceEquals(this, other)
        || other is ConstructedType constructed
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

    public static TypeConstructor List { get; } = new("list", 1);

    public static TypeConstructor Map { get; } = new("map", 2);

    public static TypeConstructor Option { get; } = new("option", 1);

    public static IReadOnlyList<PyropType> MapKeyTypes { get; } = [PyropType.Int, PyropType.String, PyropType.Bool];

    public static IReadOnlyDictionary<string, TypeConstructor> ValueConstructors { get; } =
        ValueTypes.Cast<ConstructedType>()
            .Select(type => type.Constructor)
            .Append(List)
            .Append(Map)
            .Append(Option)
            .ToDictionary(constructor => constructor.Name);

    public static IEnumerable<string> Names => ValueConstructors.Keys.Append(PyropType.Void.Name);

    public static PyropType ListOf(PyropType element) => new ConstructedType(List, [element]);

    public static PyropType MapOf(PyropType key, PyropType value) => new ConstructedType(Map, [key, value]);

    public static PyropType OptionOf(PyropType content) => new ConstructedType(Option, [content]);

    public static bool IsList(PyropType type) => type is ConstructedType constructed && constructed.Constructor == List;

    public static bool IsMap(PyropType type) => type is ConstructedType constructed && constructed.Constructor == Map;

    public static bool IsOption(PyropType type) => type is ConstructedType constructed && constructed.Constructor == Option;

    public static bool IsCollection(PyropType type) => IsList(type) || IsMap(type);

    public static bool IsMapKey(PyropType type) => MapKeyTypes.Contains(type) || type is ChoiceType { HasData: false };

    public static IReadOnlyList<string> NamesOf(params PyropType[] types)
    {
        var ambiguous = types
            .SelectMany(type => type.DeclaredTypes)
            .Distinct()
            .GroupBy(declared => declared.Name)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();
        return types.Select(type => type.NameQualifying(ambiguous)).ToList();
    }

    public static string DescribeMapKeyTypes() => Describe([.. MapKeyTypes.Select(type => type.Name), "a choice"], "or");

    private static string Describe(IReadOnlyList<string> names, string conjunction) =>
        $"{string.Join(", ", names.SkipLast(1))} {conjunction} {names[^1]}";
}
