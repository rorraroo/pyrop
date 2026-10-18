using Pyrop.Runtime;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Pyrop.Cli;

public sealed class ConsoleProgramOutput(
    TextWriter writer,
    bool plain,
    bool inputEchoed = false,
    string newLine = "\n",
    ConsoleStream? errors = null,
    bool sharedScreen = false,
    IAnsiConsole? richConsole = null,
    bool selectable = false) : IProgramOutput
{
    private const string MoreEntries = "(Move up and down to see more entries)";

    private readonly ConsoleStream _output = new(writer, plain, newLine);
    private readonly ConsoleStream _errors = errors ?? ConsoleStream.Null;
    private ConsoleStream? _lastWritten;

    public bool AtLineStart => _output.AtLineStart;

    public bool ErrorsAtLineStart => _errors.AtLineStart;

    public void Write(string text, OutputStyle style) => WriteTo(_output, _errors, text, style);

    public void WriteError(string text, OutputStyle style) => WriteTo(_errors, _output, text, style);

    public void WritePrompt(string prompt, string? hint, OutputStyle style)
    {
        if (!plain)
        {
            Write("? ", OutputStyle.Info);
        }

        Write(prompt, style);
        Write(hint ?? "", OutputStyle.Muted);
    }

    public void LineRead()
    {
        if (!inputEchoed)
        {
            return;
        }

        _output.AtLineStart = true;
        if (sharedScreen || _errors.OnScreen)
        {
            _errors.AtLineStart = true;
        }
    }

    public int? TryChoose(string prompt, IReadOnlyList<string> entries, OutputStyle style)
    {
        if (plain || !selectable || richConsole is null)
        {
            return null;
        }

        var question = prompt.TrimEnd();
        var selection = new SelectionPrompt<int>()
            .Title($"{Marked("?", OutputStyle.Info)} {Marked(question, style)}")
            .PageSize(Math.Clamp(entries.Count, 3, Math.Max(3, richConsole.Profile.Height - 5)))
            .MoreChoicesText(Marked(MoreEntries, OutputStyle.Muted))
            .UseConverter(index => Marked(entries[index], style))
            .AddChoices(Enumerable.Range(0, entries.Count));

        EnsureLineStart();
        ConsoleCancelEventHandler showCursor = (_, _) => richConsole.Cursor.Show();
        Console.CancelKeyPress += showCursor;
        int picked;
        try
        {
            picked = richConsole.Prompt(selection);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        finally
        {
            Console.CancelKeyPress -= showCursor;
        }

        WritePrompt(question + " ", null, style);
        Write(entries[picked] + "\n", OutputStyle.None);
        return picked;
    }

    public bool TryWriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, OutputStyle style)
    {
        if (plain || richConsole is null)
        {
            return false;
        }

        var cellStyle = RichStyle(style);
        var headerStyle = cellStyle.Combine(new Style(decoration: Decoration.Bold));
        var headerCells = headers.Select(header => new Text(header, headerStyle)).ToList();
        var rowCells = rows.Select(row => row.Select(cell => new Text(cell, cellStyle)).ToList()).ToList();
        if (!FitsOnScreen(richConsole, headerCells, rowCells))
        {
            return false;
        }

        var table = new Table().BorderStyle(cellStyle);
        foreach (var header in headerCells)
        {
            table.AddColumn(new TableColumn(header));
        }

        foreach (var row in rowCells)
        {
            table.AddRow(row);
        }

        EnsureLineStart();
        richConsole.Write(table);
        _output.AtLineStart = true;
        _lastWritten = _output;
        if (sharedScreen)
        {
            _errors.AtLineStart = true;
        }

        return true;
    }

    public void EnsureLineStart()
    {
        if (!AtLineStart)
        {
            Write("\n", OutputStyle.None);
        }
    }

    public void BeginErrorMessage()
    {
        var unfinished = sharedScreen ? _lastWritten : _errors;
        if (unfinished is { AtLineStart: false })
        {
            WriteTo(unfinished, unfinished == _output ? _errors : _output, "\n", OutputStyle.None);
        }

        _errors.AtLineStart = true;
    }

    private static bool FitsOnScreen(IAnsiConsole console, List<Text> headers, List<List<Text>> rows)
    {
        var options = RenderOptions.Create(console);
        var narrowest = 3L * headers.Count + 1;
        for (var column = 0; column < headers.Count; column++)
        {
            var index = column;
            narrowest += rows
                .Select(row => row[index])
                .Prepend(headers[index])
                .Max(cell => ((IRenderable)cell).Measure(options, int.MaxValue).Min);
        }

        return narrowest <= console.Profile.Width;
    }

    private static string? MarkupTag(OutputStyle style) => style switch
    {
        OutputStyle.Emphasis => "bold",
        OutputStyle.Muted => "grey",
        OutputStyle.Success => "green",
        OutputStyle.Warning => "olive",
        OutputStyle.Danger => "maroon",
        OutputStyle.Info => "teal",
        _ => null,
    };

    private static Style RichStyle(OutputStyle style) => MarkupTag(style) is { } tag ? Style.Parse(tag) : Style.Plain;

    private static string Marked(string text, OutputStyle style) =>
        MarkupTag(style) is { } tag ? $"[{tag}]{Markup.Escape(text)}[/]" : Markup.Escape(text);

    private void WriteTo(ConsoleStream stream, ConsoleStream other, string text, OutputStyle style)
    {
        stream.Write(text, style);
        if (text.Length == 0)
        {
            return;
        }

        _lastWritten = stream;
        if (sharedScreen)
        {
            other.AtLineStart = stream.AtLineStart;
        }
    }
}
