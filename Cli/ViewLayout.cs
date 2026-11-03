using System.Globalization;
using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Cli;

public readonly record struct Segment(string Text, OutputStyle Style, bool Inverted = false);

public sealed record ScreenLine(IReadOnlyList<Segment> Segments)
{
    private int? _width;

    public static ScreenLine Empty { get; } = new([]);

    public int Width => _width ??= Segments.Sum(segment => ViewLayout.WidthOf(segment.Text));

    public ScreenLine CutTo(int width)
    {
        if (Width <= width)
        {
            return this;
        }

        var kept = new List<Segment>();
        var left = width;
        foreach (var segment in Segments)
        {
            if (left <= 0)
            {
                break;
            }

            var segmentWidth = ViewLayout.WidthOf(segment.Text);
            kept.Add(segmentWidth <= left ? segment : segment with { Text = ViewLayout.Slice(segment.Text, 0, left) });
            left -= segmentWidth;
        }

        return new ScreenLine(kept);
    }

    public string PlainText => string.Concat(Segments.Select(segment => segment.Text));

    public ScreenLine PaddedTo(int width) =>
        Width >= width ? this : new ScreenLine([.. Segments, new Segment(new string(' ', width - Width), OutputStyle.None)]);

    public string ToAnsi()
    {
        var builder = new StringBuilder();
        foreach (var segment in Segments)
        {
            var code = ViewLayout.CodeOf(segment.Style, segment.Inverted);
            builder.Append(code is null ? segment.Text : $"\e[{code}m{segment.Text}\e[0m");
        }

        return builder.ToString();
    }
}

public static class ViewLayout
{
    public const string TooSmall = "Make the window larger to see this screen.";
    public const int SmallestWidth = 20;
    public const int SmallestHeight = 3;

    private const int ColumnGap = 1;
    private const int TableGap = 2;

    public static IReadOnlyList<ScreenLine> Screen(ViewNode view, OutputStyle style, int terminalWidth, int height)
    {
        view = Sanitized(view);
        var width = terminalWidth - 1;
        if (terminalWidth < SmallestWidth || height < SmallestHeight || FixedHeight(view, width) > height)
        {
            return Wrap(TooSmall, Math.Max(width, 1)).Take(Math.Max(height, 1)).Select(line => Line(line, OutputStyle.None)).ToList();
        }

        return Render(view, width, height, style).Take(height).Select(line => line.CutTo(width)).ToList();
    }

    public static IReadOnlyList<ScreenLine> Page(ViewNode view, OutputStyle style, int terminalWidth)
    {
        view = Sanitized(view);
        var width = Math.Max(terminalWidth - 1, 1);
        return Render(view, width, null, style).Select(line => line.CutTo(width)).ToList();
    }

    private static ViewNode Sanitized(ViewNode view) => view switch
    {
        TextView text => new TextView(Clean(text.Text, keepNewLines: true)),
        TableView table => table with
        {
            Headers = table.Headers.Select(header => Clean(header)).ToList(),
            Rows = table.Rows.Select(row => (IReadOnlyList<string>)row.Select(cell => Clean(cell)).ToList()).ToList(),
        },
        MenuView menu => menu with { Entries = menu.Entries.Select(entry => Clean(entry)).ToList() },
        BarView bar => bar with { Label = Clean(bar.Label) },
        PanelView panel => new PanelView(Clean(panel.Title), Sanitized(panel.Content)),
        StackView stack => stack with { Views = stack.Views.Select(Sanitized).ToList() },
        GridView grid => grid with { Lines = grid.Lines.Select(line => Clean(line)).ToList() },
        StyledView styled => styled with { Content = Sanitized(styled.Content) },
        _ => view,
    };

    public static string Clean(string text, bool keepNewLines = false)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character == '\n' && keepNewLines ? '\n' : character == '\r' && keepNewLines ? "" : char.IsControl(character) ? " " : character);
        }

        return builder.ToString();
    }

    public static int WidthOf(string text) => new StringInfo(text).LengthInTextElements;

    public static string? CodeOf(OutputStyle style, bool inverted)
    {
        var code = style switch
        {
            OutputStyle.Emphasis => "1",
            OutputStyle.Muted => "90",
            OutputStyle.Success => "32",
            OutputStyle.Warning => "33",
            OutputStyle.Danger => "31",
            OutputStyle.Info => "36",
            _ => null,
        };

        return inverted ? (code is null ? "7" : $"{code};7") : code;
    }

    private static List<ScreenLine> Render(ViewNode view, int width, int? height, OutputStyle style)
    {
        width = Math.Max(width, 1);
        return view switch
        {
            TextView text => Wrap(text.Text, width).Select(line => Line(line, style)).ToList(),
            BarView bar => [RenderBar(bar, width, style)],
            MenuView menu => RenderMenu(menu, width, height, style),
            TableView table => RenderTable(table, width, height, style),
            GridView grid => RenderGrid(grid, width, height, style),
            PanelView panel => RenderPanel(panel, width, height, style),
            StackView { SideBySide: true } stack => RenderColumns(stack, width, height, style),
            StackView stack => RenderRows(stack, width, height, style),
            StyledView styled => Render(styled.Content, width, height, styled.Style),
            _ => throw new InvalidOperationException($"Unknown view {view.GetType().Name}."),
        };
    }

    private static int FixedHeight(ViewNode view, int width)
    {
        width = Math.Max(width, 1);
        return view switch
        {
            TextView text => Wrap(text.Text, width).Count,
            BarView => 1,
            MenuView menu => menu.Entries.Count > 0 ? 1 : 0,
            TableView table => TableHeader(table, ColumnWidths(table, width)).Count + 1 + (table.Rows.Count > 0 ? 1 : 0),
            GridView grid => grid.Lines.Count > 0 ? 1 : 0,
            PanelView panel => 2 + FixedHeight(panel.Content, width - 4),
            StackView { SideBySide: true } stack => stack.Views.Count == 0
                ? 0
                : stack.Views.Zip(ColumnShares(stack.Views.Count, width)).Max(pair => FixedHeight(pair.First, pair.Second)),
            StackView stack => stack.Views.Sum(inner => FixedHeight(inner, width)),
            StyledView styled => FixedHeight(styled.Content, width),
            _ => 0,
        };
    }

    private static int NaturalHeight(ViewNode view, int width)
    {
        width = Math.Max(width, 1);
        return view switch
        {
            TextView text => Wrap(text.Text, width).Count,
            BarView => 1,
            MenuView menu => menu.Entries.Count,
            TableView table => RenderTable(table, width, null, OutputStyle.None).Count,
            GridView grid => grid.Lines.Count,
            PanelView panel => 2 + NaturalHeight(panel.Content, width - 4),
            StackView { SideBySide: true } stack => stack.Views.Count == 0
                ? 0
                : stack.Views.Zip(ColumnShares(stack.Views.Count, width)).Max(pair => NaturalHeight(pair.First, pair.Second)),
            StackView stack => stack.Views.Sum(inner => NaturalHeight(inner, width)),
            StyledView styled => NaturalHeight(styled.Content, width),
            _ => 0,
        };
    }

    private static ScreenLine RenderBar(BarView bar, int width, OutputStyle style)
    {
        var numbers = $"{bar.Value} of {bar.Maximum}";
        var barWidth = width - WidthOf(bar.Label) - WidthOf(numbers) - 2;
        if (barWidth < 1)
        {
            return Line(Cut($"{bar.Label}: {numbers}", width), style);
        }

        var filled = bar.Maximum < 1 ? 0 : (int)(Math.Clamp((double)bar.Value, 0, bar.Maximum) * barWidth / bar.Maximum);
        return new ScreenLine(
        [
            new Segment(bar.Label + " ", style),
            new Segment(new string('█', filled), style),
            new Segment(new string('░', barWidth - filled), OutputStyle.Muted),
            new Segment(" " + numbers, style),
        ]);
    }

    private static List<ScreenLine> RenderMenu(MenuView menu, int width, int? height, OutputStyle style)
    {
        var count = menu.Entries.Count;
        var available = height ?? count;
        var (start, shown) = MenuWindow(count, menu.Selected, available);
        var lines = new List<ScreenLine>();
        if (start > 0 && shown < available)
        {
            lines.Add(Line(Cut($"({start} more above)", width), OutputStyle.Muted));
        }

        for (var index = start; index < start + shown; index++)
        {
            var selected = index == menu.Selected;
            var text = Cut((selected ? "› " : "  ") + menu.Entries[index], width);
            lines.Add(new ScreenLine([new Segment(selected ? Pad(text, width) : text, style, selected)]));
        }

        var below = count - start - shown;
        if (below > 0 && lines.Count < available)
        {
            lines.Add(Line(Cut($"({below} more below)", width), OutputStyle.Muted));
        }

        return lines;
    }

    private static (int Start, int Shown) MenuWindow(int count, long selected, int available)
    {
        if (count <= available)
        {
            return (0, count);
        }

        if (available <= 0)
        {
            return (0, 0);
        }

        var focus = (int)Math.Clamp(selected, 0, count - 1);
        for (var shown = available; shown >= 1; shown--)
        {
            var start = Math.Clamp(focus - shown / 2, 0, count - shown);
            var indicators = (start > 0 ? 1 : 0) + (start + shown < count ? 1 : 0);
            if (shown + indicators <= available)
            {
                return (start, shown);
            }
        }

        return (Math.Clamp(focus, 0, count - 1), 1);
    }

    private static List<ScreenLine> RenderTable(TableView table, int width, int? height, OutputStyle style)
    {
        var widths = ColumnWidths(table, width);
        var lines = TableHeader(table, widths);
        lines.Add(Line(new string('─', Math.Min(width, widths.Sum() + TableGap * (widths.Length - 1))), OutputStyle.Muted));
        var rowsAvailable = height is { } limit ? limit - lines.Count : int.MaxValue;
        var rows = table.Rows.Select(row => TableRow(row, widths, style)).ToList();
        var used = 0;
        var still = new long[rows.Count + 1];
        for (var index = rows.Count - 1; index >= 0; index--)
        {
            still[index] = still[index + 1] + rows[index].Count;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var remaining = rows.Count - index;
            var needsNote = still[index] > rowsAvailable - used;
            var room = rowsAvailable - used - (needsNote ? 1 : 0);
            if (rows[index].Count > room)
            {
                if (rowsAvailable - used >= 1)
                {
                    lines.Add(Line(Cut(remaining == 1 ? "(1 more row)" : $"({remaining} more rows)", width), OutputStyle.Muted));
                }

                break;
            }

            lines.AddRange(rows[index]);
            used += rows[index].Count;
        }

        return lines;
    }

    private static List<ScreenLine> TableHeader(TableView table, int[] widths)
    {
        var headerStyle = OutputStyle.Emphasis;
        return TableRow(table.Headers, widths, headerStyle);
    }

    private static List<ScreenLine> TableRow(IReadOnlyList<string> cells, int[] widths, OutputStyle style)
    {
        var wrapped = cells.Select((cell, column) => Wrap(cell, widths[column])).ToList();
        var height = wrapped.Max(lines => lines.Count);
        var lines = new List<ScreenLine>(height);
        for (var line = 0; line < height; line++)
        {
            var text = new StringBuilder();
            for (var column = 0; column < cells.Count; column++)
            {
                var part = line < wrapped[column].Count ? wrapped[column][line] : "";
                text.Append(column == cells.Count - 1 ? part : Pad(part, widths[column]) + new string(' ', TableGap));
            }

            lines.Add(Line(text.ToString().TrimEnd(), style));
        }

        return lines;
    }

    private static int[] ColumnWidths(TableView table, int width)
    {
        var columns = table.Headers.Count;
        var natural = Enumerable.Range(0, columns)
            .Select(column => table.Rows.Select(row => WidthOf(row[column])).Prepend(WidthOf(table.Headers[column])).Max())
            .Select(value => Math.Max(value, 1))
            .ToArray();
        var available = Math.Max(width - TableGap * (columns - 1), columns);
        return Share(natural, available);
    }

    private static int[] Share(int[] needs, int available)
    {
        var given = new int[needs.Length];
        var open = Enumerable.Range(0, needs.Length).Where(index => needs[index] > 0).ToList();
        var left = available;
        while (open.Count > 0 && left > 0)
        {
            var share = Math.Max(left / open.Count, 1);
            var satisfied = open.Where(index => needs[index] - given[index] <= share).ToList();
            if (satisfied.Count == 0)
            {
                foreach (var index in open)
                {
                    var extra = Math.Min(share, left);
                    given[index] += extra;
                    left -= extra;
                }

                foreach (var index in open.Take(left))
                {
                    given[index]++;
                }

                break;
            }

            foreach (var index in satisfied)
            {
                left -= needs[index] - given[index];
                given[index] = needs[index];
                open.Remove(index);
            }
        }

        return given;
    }

    private static List<ScreenLine> RenderGrid(GridView grid, int width, int? height, OutputStyle style)
    {
        if (grid.Lines.Count == 0)
        {
            return [];
        }

        var longest = Math.Max(grid.Lines.Max(WidthOf), 1);
        var shownWidth = Math.Min(longest, width);
        var shownHeight = Math.Min(grid.Lines.Count, height ?? grid.Lines.Count);
        var left = Window(grid.Column, longest, shownWidth);
        var top = Window(grid.Row, grid.Lines.Count, shownHeight);
        return grid.Lines
            .Skip(top)
            .Take(shownHeight)
            .Select(line => Line(Slice(Pad(line, longest), left, shownWidth).TrimEnd(), style))
            .ToList();
    }

    private static int Window(long focus, int length, int shown)
    {
        var centre = (int)Math.Clamp(focus, 0, length - 1);
        return Math.Clamp(centre - shown / 2, 0, Math.Max(length - shown, 0));
    }

    private static List<ScreenLine> RenderPanel(PanelView panel, int width, int? height, OutputStyle style)
    {
        var inner = Math.Max(width - 4, 1);
        var content = Render(panel.Content, inner, height - 2, style);
        var titleStyle = style == OutputStyle.None ? OutputStyle.Emphasis : style;
        var title = Cut(panel.Title, Math.Max(width - 6, 0));
        var top = title.Length == 0
            ? new ScreenLine([new Segment("╭" + new string('─', Math.Max(width - 2, 0)) + "╮", style)])
            : new ScreenLine(
            [
                new Segment("╭─ ", style),
                new Segment(title, titleStyle),
                new Segment(" " + new string('─', Math.Max(width - 5 - WidthOf(title), 0)) + "╮", style),
            ]);
        var lines = new List<ScreenLine> { top };
        foreach (var line in content)
        {
            lines.Add(new ScreenLine([new Segment("│ ", style), .. line.PaddedTo(inner).Segments, new Segment(" │", style)]));
        }

        lines.Add(new ScreenLine([new Segment("╰" + new string('─', Math.Max(width - 2, 0)) + "╯", style)]));
        return lines;
    }

    private static List<ScreenLine> RenderRows(StackView stack, int width, int? height, OutputStyle style)
    {
        if (height is not { } limit)
        {
            return stack.Views.SelectMany(view => Render(view, width, null, style)).ToList();
        }

        var fixedHeights = stack.Views.Select(view => FixedHeight(view, width)).ToArray();
        var needs = stack.Views.Select((view, index) => NaturalHeight(view, width) - fixedHeights[index]).ToArray();
        var extra = Share(needs, Math.Max(limit - fixedHeights.Sum(), 0));
        return stack.Views.SelectMany((view, index) => Render(view, width, fixedHeights[index] + extra[index], style)).ToList();
    }

    private static List<ScreenLine> RenderColumns(StackView stack, int width, int? height, OutputStyle style)
    {
        if (stack.Views.Count == 0)
        {
            return [];
        }

        var widths = ColumnShares(stack.Views.Count, width);
        var rendered = stack.Views.Select((view, index) => Render(view, widths[index], height, style)).ToList();
        var count = rendered.Max(lines => lines.Count);
        var lines = new List<ScreenLine>(count);
        for (var row = 0; row < count; row++)
        {
            var segments = new List<Segment>();
            for (var column = 0; column < rendered.Count; column++)
            {
                var part = row < rendered[column].Count ? rendered[column][row] : ScreenLine.Empty;
                segments.AddRange(column == rendered.Count - 1 ? part.Segments : part.PaddedTo(widths[column]).Segments);
                if (column < rendered.Count - 1)
                {
                    segments.Add(new Segment(new string(' ', ColumnGap), OutputStyle.None));
                }
            }

            lines.Add(new ScreenLine(segments));
        }

        return lines;
    }

    private static int[] ColumnShares(int count, int width)
    {
        var available = Math.Max(width - ColumnGap * (count - 1), count);
        return Enumerable.Range(0, count).Select(index => available / count + (index < available % count ? 1 : 0)).ToArray();
    }

    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            var lineWidth = 0;
            foreach (var word in paragraph.Split(' '))
            {
                var elements = TextElements(word);
                if (elements.Count == 0 && lineWidth > 0 && lineWidth + 1 > width)
                {
                    continue;
                }

                var start = 0;
                while (true)
                {
                    var wordWidth = elements.Count - start;
                    var needed = lineWidth == 0 ? wordWidth : lineWidth + 1 + wordWidth;
                    if (needed <= width)
                    {
                        if (lineWidth > 0)
                        {
                            line.Append(' ');
                        }

                        AppendElements(line, elements, start, wordWidth);
                        lineWidth = needed;
                        break;
                    }

                    if (lineWidth > 0)
                    {
                        lines.Add(line.ToString());
                        line.Clear();
                        lineWidth = 0;
                        continue;
                    }

                    var piece = new StringBuilder();
                    AppendElements(piece, elements, start, width);
                    lines.Add(piece.ToString());
                    start += width;
                }
            }

            lines.Add(line.ToString());
        }

        return lines;
    }

    private static ScreenLine Line(string text, OutputStyle style) => new([new Segment(text, style)]);

    private static string Cut(string text, int width) => WidthOf(text) <= width ? text : Slice(text, 0, width);

    private static string Pad(string text, int width) => text + new string(' ', Math.Max(width - WidthOf(text), 0));

    private static List<string> TextElements(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        return elements;
    }

    private static void AppendElements(StringBuilder builder, List<string> elements, int start, int count)
    {
        for (var index = start; index < start + count && index < elements.Count; index++)
        {
            builder.Append(elements[index]);
        }
    }

    public static string Slice(string text, int start, int count)
    {
        var info = new StringInfo(text);
        var length = info.LengthInTextElements;
        if (start >= length || count <= 0)
        {
            return "";
        }

        return info.SubstringByTextElements(start, Math.Min(count, length - start));
    }
}
