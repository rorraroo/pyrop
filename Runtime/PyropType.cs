namespace Pyrop.Runtime;

public enum PyropType
{
    Int,
    Float,
    String,
    Bool,
    Void,
}

public static class PyropTypes
{
    public static IReadOnlyList<PyropType> ValueTypes { get; } =
        [PyropType.Int, PyropType.Float, PyropType.String, PyropType.Bool];

    public static string Name(this PyropType type) => type switch
    {
        PyropType.Int => "int",
        PyropType.Float => "float",
        PyropType.String => "string",
        PyropType.Bool => "bool",
        PyropType.Void => "void",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static PyropType? FromName(string name) => name switch
    {
        "int" => PyropType.Int,
        "float" => PyropType.Float,
        "string" => PyropType.String,
        "bool" => PyropType.Bool,
        "void" => PyropType.Void,
        _ => null,
    };
}
