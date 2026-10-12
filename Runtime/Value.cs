using System.Globalization;

namespace Pyrop.Runtime;

public abstract record Value
{
    public abstract PyropType Type { get; }

    public abstract string ToText();

    public long AsInt() => ((IntValue)this).Value;

    public double AsFloat() => ((FloatValue)this).Value;

    public string AsString() => ((StringValue)this).Value;

    public bool AsBool() => ((BoolValue)this).Value;
}

public sealed record IntValue(long Value) : Value
{
    public override PyropType Type => PyropType.Int;

    public override string ToText() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record FloatValue(double Value) : Value
{
    public override PyropType Type => PyropType.Float;

    public override string ToText()
    {
        var text = Value.ToString(CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') ? text : text + ".0";
    }
}

public sealed record StringValue(string Value) : Value
{
    public override PyropType Type => PyropType.String;

    public override string ToText() => Value;
}

public sealed record BoolValue(bool Value) : Value
{
    public static BoolValue True { get; } = new(true);

    public static BoolValue False { get; } = new(false);

    public static BoolValue Of(bool value) => value ? True : False;

    public override PyropType Type => PyropType.Bool;

    public override string ToText() => Value ? "true" : "false";
}
