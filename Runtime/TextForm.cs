using System.Runtime.CompilerServices;
using System.Text;

namespace Pyrop.Runtime;

public static class TextForm
{
    public static string Of(
        Value value, Func<string, Exception> fail, int nestingLimit = ValueEquality.MaxNesting, Func<string, Exception>? tooDeep = null)
    {
        if (value is not (ListValue or MapValue or RecordValue or OptionValue))
        {
            return value.ToText();
        }

        var builder = new StringBuilder();
        Append(builder, value, fail, tooDeep ?? fail, nestingLimit, 0);
        return builder.ToString();
    }

    public static string OfItem(Value value) => value is StringValue text ? Quote(text.Value) : value.ToText();

    public static string Quote(string text)
    {
        var builder = new StringBuilder(text.Length + 2);
        AppendQuoted(builder, text, null);
        return builder.ToString();
    }

    private static void Append(
        StringBuilder builder, Value value, Func<string, Exception> fail, Func<string, Exception> tooDeep, int limit, int depth)
    {
        if (value is ListValue or MapValue or RecordValue or OptionValue && (depth >= limit || !RuntimeHelpers.TryEnsureSufficientExecutionStack()))
        {
            throw tooDeep("This value is nested too deeply to be written as text.");
        }

        switch (value)
        {
            case RecordValue record:
                CheckLength(builder.Length + (long)record.Record.Name.Length, fail);
                builder.Append(record.Record.Name);
                for (var index = 0; index < record.Fields.Length; index++)
                {
                    var name = record.Record.Fields[index].Name;
                    CheckLength(builder.Length + (long)name.Length + 2, fail);
                    builder.Append(' ').Append(name).Append(' ');
                    AppendInner(builder, record.Fields[index], fail, tooDeep, limit, depth + 1);
                }

                break;
            case OptionValue { Content: null }:
                CheckLength(builder.Length + 4L, fail);
                builder.Append("none");
                break;
            case OptionValue { Content: { } content }:
                CheckLength(builder.Length + 5L, fail);
                builder.Append("some ");
                AppendInner(builder, content, fail, tooDeep, limit, depth + 1);
                break;
            case ListValue list:
                builder.Append('[');
                var firstItem = true;
                foreach (var item in list.Items)
                {
                    if (!firstItem)
                    {
                        builder.Append(", ");
                    }

                    firstItem = false;
                    Append(builder, item, fail, tooDeep, limit, depth + 1);
                }

                builder.Append(']');
                break;
            case MapValue map:
                builder.Append('{');
                var firstEntry = true;
                foreach (var (key, entry) in map.Entries)
                {
                    if (!firstEntry)
                    {
                        builder.Append(", ");
                    }

                    firstEntry = false;
                    Append(builder, key, fail, tooDeep, limit, depth + 1);
                    builder.Append(": ");
                    Append(builder, entry, fail, tooDeep, limit, depth + 1);
                }

                builder.Append('}');
                break;
            case StringValue raw:
                AppendQuoted(builder, raw.Value, fail);
                break;
            default:
                var text = value.ToText();
                CheckLength(builder.Length + (long)text.Length, fail);
                builder.Append(text);
                break;
        }

        CheckLength(builder.Length, fail);
    }

    private static void AppendInner(
        StringBuilder builder, Value value, Func<string, Exception> fail, Func<string, Exception> tooDeep, int limit, int depth)
    {
        var bracketed = value is RecordValue;
        builder.Append(bracketed ? "(" : "");
        Append(builder, value, fail, tooDeep, limit, depth);
        builder.Append(bracketed ? ")" : "");
    }

    private static void AppendQuoted(StringBuilder builder, string text, Func<string, Exception>? fail)
    {
        CheckLength(builder.Length + (long)text.Length + 2, fail);
        builder.Append('"');
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var escape = text[index] switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => null,
            };
            if (escape is null)
            {
                continue;
            }

            builder.Append(text, start, index - start).Append(escape);
            start = index + 1;
            CheckLength(builder.Length + (long)(text.Length - start) + 1, fail);
        }

        builder.Append(text, start, text.Length - start).Append('"');
    }

    private static void CheckLength(long length, Func<string, Exception>? fail)
    {
        if (fail is not null && length > TextOperations.MaxLength)
        {
            throw fail("The text form of this value would exceed the largest allowed text size.");
        }
    }
}
