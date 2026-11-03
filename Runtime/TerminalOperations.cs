using System.Globalization;
using System.Text;

namespace Pyrop.Runtime;

public static class TerminalOperations
{
    private const string ColumnGap = "  ";

    public static string Choose(IBuiltinInvocation invocation, string prompt, ListValue entries)
    {
        if (entries.Count == 0)
        {
            throw invocation.Fail("'choose' needs at least one entry.");
        }

        var texts = Texts(entries);
        if (!invocation.Input.HasPendingLine && invocation.Output.TryChoose(prompt, texts, invocation.Style) is { } picked)
        {
            return texts[picked];
        }

        return ChooseByNumber(invocation, prompt, texts);
    }

    public static Value? WriteTable(IBuiltinInvocation invocation, ListValue headers, ListValue rows)
    {
        var (headerTexts, rowTexts, lines, lengths, widths) = CheckedTable(invocation, headers, rows, asView: false);
        if (!invocation.Output.TryWriteTable(headerTexts, rowTexts, invocation.Style))
        {
            invocation.Output.EnsureLineStart();
            for (var index = 0; index < lines.Count; index++)
            {
                invocation.Output.Write(PlainLine(lines[index], lengths[index], widths), invocation.Style);
            }
        }

        return null;
    }

    public static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string> PlainLines) TableParts(
        IBuiltinInvocation invocation, ListValue headers, ListValue rows, bool asView)
    {
        var (headerTexts, rowTexts, lines, lengths, widths) = CheckedTable(invocation, headers, rows, asView);
        return (headerTexts, rowTexts, lines.Select((line, index) => PlainLine(line, lengths[index], widths)).ToList());
    }

    private static (List<string> Headers, List<IReadOnlyList<string>> Rows, List<IReadOnlyList<string>> Lines, List<long[]> Lengths, long[] Widths) CheckedTable(
        IBuiltinInvocation invocation, ListValue headers, ListValue rows, bool asView)
    {
        if (headers.Count == 0)
        {
            throw invocation.Fail(asView ? "A table needs at least one header." : "'write_table' needs at least one header.");
        }

        var headerTexts = Texts(headers);
        var rowTexts = new List<IReadOnlyList<string>>(rows.Count);
        foreach (var row in rows.Items)
        {
            var cells = row.AsList();
            if (cells.Count != headers.Count)
            {
                throw invocation.Fail(
                    $"Row {rowTexts.Count} of {(asView ? "a" : "the")} table has {Counted(cells.Count, "cell")}, but the table has {Counted(headers.Count, "header")}; rows are numbered from 0.");
            }

            rowTexts.Add(Texts(cells));
        }

        var lines = new List<IReadOnlyList<string>>(rowTexts.Count + 1) { headerTexts };
        lines.AddRange(rowTexts);
        var lengths = lines.Select(line => line.Select(TextOperations.LengthOf).ToArray()).ToList();
        var widths = Enumerable.Range(0, headers.Count).Select(column => lengths.Max(line => line[column])).ToArray();
        if (lines.Select((line, index) => PlainLength(line, lengths[index], widths)).Any(length => length > TextOperations.MaxLength))
        {
            throw invocation.Fail(asView ? "A table would exceed the largest allowed text size." : "The table of 'write_table' would exceed the largest allowed text size.");
        }

        return (headerTexts, rowTexts, lines, lengths, widths);
    }

    private static string ChooseByNumber(IBuiltinInvocation invocation, string prompt, IReadOnlyList<string> entries)
    {
        var output = invocation.Output;
        var count = entries.Count.ToString(CultureInfo.InvariantCulture);
        var range = entries.Count == 1 ? "number 1" : $"a number from 1 to {count}";
        output.EnsureLineStart();
        output.WritePrompt(prompt.TrimEnd() + "\n", null, invocation.Style);
        for (var index = 0; index < entries.Count; index++)
        {
            var number = (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(count.Length);
            output.Write($"{number}. {entries[index]}\n", invocation.Style);
        }

        while (true)
        {
            output.Write($"Choose {range}: ", invocation.Style);
            var answer = invocation.Input.ReadLine() ?? throw invocation.Fail("'choose' reached the end of input.");
            output.LineRead();
            if (NumberIn(answer, entries.Count) is { } chosen)
            {
                return entries[(int)chosen - 1];
            }

            output.Write($"Please choose {range}.\n", OutputStyle.Warning);
        }
    }

    private static long? NumberIn(string answer, int count)
    {
        var trimmed = answer.Trim();
        return trimmed.Length > 0
            && trimmed.All(char.IsAsciiDigit)
            && long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= 1
            && number <= count
                ? number
                : null;
    }

    private static List<string> Texts(ListValue list) => list.Items.Select(item => item.AsString()).ToList();

    private static string Counted(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static long PlainLength(IReadOnlyList<string> cells, long[] lengths, long[] widths)
    {
        var length = (long)cells[^1].Length;
        for (var column = 0; column < cells.Count - 1; column++)
        {
            length += cells[column].Length + widths[column] - lengths[column] + ColumnGap.Length;
        }

        return length;
    }

    private static string PlainLine(IReadOnlyList<string> cells, long[] lengths, long[] widths)
    {
        var line = new StringBuilder((int)PlainLength(cells, lengths, widths) + 1);
        for (var column = 0; column < cells.Count - 1; column++)
        {
            line.Append(cells[column]).Append(' ', (int)(widths[column] - lengths[column])).Append(ColumnGap);
        }

        return line.Append(cells[^1]).Append('\n').ToString();
    }
}
