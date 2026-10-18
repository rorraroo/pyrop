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

        if (from == to || to == PyropType.String || (from == PyropType.String && to is ChoiceType))
        {
            return true;
        }

        if (!PyropTypes.ValueTypes.Contains(from) || !PyropTypes.ValueTypes.Contains(to))
        {
            return false;
        }

        return from == PyropType.String
            || (from == PyropType.Int && to == PyropType.Float)
            || (from == PyropType.Float && to == PyropType.Int);
    }

    public static Value Convert(
        Value value, PyropType target, Func<string, Exception> fail, int nestingLimit = ValueEquality.MaxNesting, Func<string, Exception>? tooDeep = null)
    {
        if (value.Type == target)
        {
            return value;
        }

        return value switch
        {
            _ when target == PyropType.String => new StringValue(TextForm.Of(value, fail, nestingLimit, tooDeep)),
            IntValue integer when target == PyropType.Float => new FloatValue(integer.Value),
            FloatValue number when target == PyropType.Int => FloatToInt(number.Value, fail),
            StringValue text when target == PyropType.Int => StringToInt(text.Value, fail),
            StringValue text when target == PyropType.Float => StringToFloat(text.Value, fail),
            StringValue text when target == PyropType.Bool => StringToBool(text.Value, fail),
            StringValue text when target is ChoiceType choice => StringToChoice(text.Value, choice, fail),
            _ => throw new InvalidOperationException(
                $"Conversion from {value.Type.Name} to {target.Name} should have been rejected by the type checker."),
        };
    }

    private const double SmallestIntAsFloat = -9_223_372_036_854_775_808.0;
    private const double FirstFloatAboveInt = 9_223_372_036_854_775_808.0;

    public static IntValue FloatToInt(double value, Func<string, Exception> fail)
    {
        var truncated = Math.Truncate(value);
        if (truncated < SmallestIntAsFloat || truncated >= FirstFloatAboveInt)
        {
            throw fail($"Float {new FloatValue(value).ToText()} is out of range for int.");
        }

        return new IntValue((long)truncated);
    }

    private static IntValue StringToInt(string text, Func<string, Exception> fail) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
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

    private static ChoiceValue StringToChoice(string text, ChoiceType choice, Func<string, Exception> fail) =>
        choice.FindVariant(text.Trim()) is { } variant
            ? choice.ValueOf(variant.Index)
            : throw fail(
                $"Cannot convert \"{text}\" to {choice.Name}; {(choice.Variants.Count == 1 ? "its only variant is" : "its variants are")} {choice.ListVariants()}.");
}
