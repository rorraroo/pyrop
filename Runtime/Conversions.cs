using System.Globalization;

namespace Pyrop.Runtime;

public static class Conversions
{
    public static bool IsAllowed(PyropType from, PyropType to)
    {
        if (from == PyropType.Void || to == PyropType.Void)
        {
            return false;
        }

        if (from == to)
        {
            return true;
        }

        if (!PyropTypes.ValueTypes.Contains(from) || !PyropTypes.ValueTypes.Contains(to))
        {
            return false;
        }

        return to == PyropType.String
            || from == PyropType.String
            || (from == PyropType.Int && to == PyropType.Float)
            || (from == PyropType.Float && to == PyropType.Int);
    }

    public static Value Convert(Value value, PyropType target, Func<string, Exception> fail)
    {
        if (value.Type == target)
        {
            return value;
        }

        return value switch
        {
            _ when target == PyropType.String => new StringValue(value.ToText()),
            IntValue integer when target == PyropType.Float => new FloatValue(integer.Value),
            FloatValue number when target == PyropType.Int => FloatToInt(number.Value, fail),
            StringValue text when target == PyropType.Int => StringToInt(text.Value, fail),
            StringValue text when target == PyropType.Float => StringToFloat(text.Value, fail),
            StringValue text when target == PyropType.Bool => StringToBool(text.Value, fail),
            _ => throw new InvalidOperationException(
                $"Conversion from {value.Type.Name} to {target.Name} should have been rejected by the type checker."),
        };
    }

    private static IntValue FloatToInt(double value, Func<string, Exception> fail)
    {
        var truncated = Math.Truncate(value);
        if (truncated < int.MinValue || truncated > int.MaxValue)
        {
            throw fail($"Float {new FloatValue(value).ToText()} is out of range for int.");
        }

        return new IntValue((int)truncated);
    }

    private static IntValue StringToInt(string text, Func<string, Exception> fail) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? new IntValue(result)
            : throw fail($"Cannot convert \"{text}\" to int.");

    private static FloatValue StringToFloat(string text, Func<string, Exception> fail) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && double.IsFinite(result)
            ? new FloatValue(result)
            : throw fail($"Cannot convert \"{text}\" to float.");

    private static BoolValue StringToBool(string text, Func<string, Exception> fail) => text.Trim() switch
    {
        "true" => BoolValue.True,
        "false" => BoolValue.False,
        _ => throw fail($"Cannot convert \"{text}\" to bool; expected \"true\" or \"false\"."),
    };
}
