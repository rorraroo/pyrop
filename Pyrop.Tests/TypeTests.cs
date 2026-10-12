using Pyrop.Runtime;

namespace Pyrop.Tests;

public class TypeTests
{
    private static readonly TypeConstructor List = new("list", 1);
    private static readonly TypeConstructor Map = new("map", 2);

    private static PyropType ListOf(PyropType element) => new ConstructedType(List, [element]);

    private static PyropType MapOf(PyropType key, PyropType value) => new ConstructedType(Map, [key, value]);

    [Fact]
    public void ComparesConstructedTypesStructurally()
    {
        Assert.Equal(ListOf(PyropType.Int), ListOf(PyropType.Int));
        Assert.True(ListOf(ListOf(PyropType.Int)) == ListOf(ListOf(PyropType.Int)));
        Assert.Equal(ListOf(PyropType.Int).GetHashCode(), ListOf(PyropType.Int).GetHashCode());
        Assert.NotEqual(ListOf(PyropType.Int), ListOf(PyropType.Float));
        Assert.NotEqual(PyropType.Int, ListOf(PyropType.Int));
    }

    [Fact]
    public void PrimitivesAreNullaryConstructedTypes()
    {
        Assert.Equal(PyropType.Int, new ConstructedType(new TypeConstructor("int", 0), []));
        Assert.NotEqual(PyropType.Int, PyropType.Float);
    }

    [Fact]
    public void NamesTypesInPrefixForm()
    {
        Assert.Equal("map string list int", MapOf(PyropType.String, ListOf(PyropType.Int)).Name);
    }

    [Fact]
    public void RejectsWrongNumberOfTypeArguments()
    {
        Assert.Throws<ArgumentException>(() => new ConstructedType(Map, [PyropType.Int]));
    }

    [Fact]
    public void MatchesOverloadsOverAFamilyOfTypes()
    {
        var element = new TypeParameter("T");
        var operation = new BuiltinOperation("first", 1,
        [
            new BuiltinOverload([ListOf(element)], element, invocation => invocation.Argument(0)),
        ]);

        var match = operation.FindOverload([ListOf(ListOf(PyropType.String))]);

        Assert.NotNull(match);
        Assert.Equal(ListOf(PyropType.String), match.Result);
        Assert.Null(operation.FindOverload([PyropType.Int]));
    }

    [Fact]
    public void RequiresConsistentBindingsForARepeatedParameter()
    {
        var element = new TypeParameter("T");
        var operation = new BuiltinOperation("push", 2,
        [
            new BuiltinOverload([ListOf(element), element], ListOf(element), invocation => invocation.Argument(0)),
        ]);

        Assert.NotNull(operation.FindOverload([ListOf(PyropType.Int), PyropType.Int]));
        Assert.Null(operation.FindOverload([ListOf(PyropType.Int), PyropType.String]));
    }
}
