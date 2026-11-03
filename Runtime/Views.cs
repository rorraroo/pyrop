using Pyrop.Parsing;

namespace Pyrop.Runtime;

public static class Views
{
    public const int MaxDepth = 1000;

    public const string ChoiceName = "View";

    public const int Text = 0;
    public const int Table = 1;
    public const int Menu = 2;
    public const int Bar = 3;
    public const int Panel = 4;
    public const int Rows = 5;
    public const int Columns = 6;
    public const int Grid = 7;
    public const int InStyle = 8;

    public static ChoiceType Declare(Module module)
    {
        var position = new SourcePosition(0, 0);
        var view = new ChoiceType(ChoiceName, module, position, isExported: true,
        [
            new("text", position, 1),
            new("table", position, 2),
            new("menu", position, 2),
            new("bar", position, 3),
            new("panel", position, 2),
            new("rows", position, 1),
            new("columns", position, 1),
            new("grid", position, 3),
            new("in_style", position, 2),
        ]);
        var s = PyropType.String;
        var i = PyropType.Int;
        var strings = PyropTypes.ListOf(s);
        var views = PyropTypes.ListOf(view);
        PyropType[][] data =
        [
            [s],
            [strings, PyropTypes.ListOf(strings)],
            [strings, i],
            [s, i, i],
            [s, view],
            [views],
            [views],
            [strings, i, i],
            [s, view],
        ];
        foreach (var (variant, types) in view.Variants.Zip(data))
        {
            variant.Define(types);
        }

        return view;
    }

    public static ChoiceType Choice => LibraryOperations.TerminalView;

    public static bool IsInStyle(ChoiceVariant variant) => variant.Choice == Choice && variant.Index == InStyle;

    public static Value? Show(IBuiltinInvocation invocation, Value view)
    {
        var node = Build(invocation, (ChoiceValue)view, 0);
        if (invocation.Output.TryShow(node, invocation.Style))
        {
            return null;
        }

        invocation.Output.EnsureLineStart();
        WritePlain(invocation, node);
        invocation.Output.Write("\n", OutputStyle.None);
        return null;
    }

    public static Value? WriteView(IBuiltinInvocation invocation, Value view)
    {
        var node = Build(invocation, (ChoiceValue)view, 0);
        if (invocation.Output.TryWriteView(node, invocation.Style))
        {
            return null;
        }

        invocation.Output.EnsureLineStart();
        WritePlain(invocation, node);
        return null;
    }

    public static IEnumerable<(string Text, OutputStyle Style)> PlainLines(ViewNode view, OutputStyle style) => view switch
    {
        TextView text => [(text.Text + "\n", style)],
        TableView table => table.PlainLines.Select(line => (line, style)),
        MenuView menu => menu.Entries.Select((entry, index) => (entry + (index == menu.Selected ? " (selected)" : "") + "\n", style)),
        BarView bar => [($"{bar.Label}: {bar.Value} of {bar.Maximum}\n", style)],
        PanelView panel => PlainLines(panel.Content, style).Prepend((panel.Title + "\n", style)),
        StackView stack => stack.Views.SelectMany(inner => PlainLines(inner, style)),
        GridView grid => grid.Lines.Select(line => (line + "\n", style)),
        StyledView styled => PlainLines(styled.Content, styled.Style),
        _ => throw new InvalidOperationException($"Unknown view {view.GetType().Name}."),
    };

    private static void WritePlain(IBuiltinInvocation invocation, ViewNode node)
    {
        foreach (var (text, style) in PlainLines(node, invocation.Style))
        {
            invocation.Output.Write(text, style);
        }
    }

    private static ViewNode Build(IBuiltinInvocation invocation, ChoiceValue view, int depth)
    {
        if (depth >= MaxDepth)
        {
            throw invocation.Fail("This view is nested too deeply to be shown.");
        }

        var data = view.Data;
        switch (view.Index)
        {
            case Text:
                return new TextView(data[0].AsString());
            case Table:
                var (headers, rows, plainLines) = TerminalOperations.TableParts(invocation, data[0].AsList(), data[1].AsList(), asView: true);
                return new TableView(headers, rows, plainLines);
            case Menu:
                return new MenuView(Strings(data[0]), data[1].AsInt());
            case Bar:
                return new BarView(data[0].AsString(), data[1].AsInt(), data[2].AsInt());
            case Panel:
                return new PanelView(data[0].AsString(), Build(invocation, (ChoiceValue)data[1], depth + 1));
            case Rows or Columns:
                return new StackView(
                    view.Index == Columns,
                    data[0].AsList().Items.Select(inner => Build(invocation, (ChoiceValue)inner, depth + 1)).ToList());
            case Grid:
                return new GridView(Strings(data[0]), data[1].AsInt(), data[2].AsInt());
            case InStyle:
                var name = data[0].AsString();
                var style = OutputStyles.Find(name) ?? throw invocation.Fail(OutputStyles.UnknownMessage(name));
                return new StyledView(style, Build(invocation, (ChoiceValue)data[1], depth + 1));
            default:
                throw new InvalidOperationException($"Unknown view kind {view.Name}.");
        }
    }

    private static IReadOnlyList<string> Strings(Value list) => list.AsList().Items.Select(item => item.AsString()).ToList();
}

public abstract record ViewNode;

public sealed record TextView(string Text) : ViewNode;

public sealed record TableView(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string> PlainLines)
    : ViewNode;

public sealed record MenuView(IReadOnlyList<string> Entries, long Selected) : ViewNode;

public sealed record BarView(string Label, long Value, long Maximum) : ViewNode;

public sealed record PanelView(string Title, ViewNode Content) : ViewNode;

public sealed record StackView(bool SideBySide, IReadOnlyList<ViewNode> Views) : ViewNode;

public sealed record GridView(IReadOnlyList<string> Lines, long Column, long Row) : ViewNode;

public sealed record StyledView(OutputStyle Style, ViewNode Content) : ViewNode;
