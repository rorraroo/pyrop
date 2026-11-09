using System.Diagnostics;
using System.Text;
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
    bool selectable = false,
    ITerminalScreen? screen = null,
    bool keys = false) : IProgramOutput
{
    private const string MoreEntries = "(Move up and down to see more entries)";
    private const string EnterAlternateScreen = "\e[?1049h\e[?25l\e[2J";
    private const string LeaveAlternateScreen = "\e[?25h\e[?1049l";
    private const int KeyPollMilliseconds = 15;

    private readonly ConsoleStream _output = new(writer, plain, newLine);
    private readonly ConsoleStream _errors = errors ?? ConsoleStream.Null;
    private ConsoleStream? _lastWritten;
    private bool _onScreen;
    private (ViewNode View, OutputStyle Style)? _shown;
    private string[]? _frame;
    private (int Width, int Height) _frameSize;
    private ConsoleCancelEventHandler? _restoreOnCancel;

    private long _written;

    public bool OnAlternateScreen => _onScreen;

    public long Written => _written;

    public bool AtLineStart => _output.AtLineStart;

    public bool ErrorsAtLineStart => _errors.AtLineStart;

    public void Write(string text, OutputStyle style)
    {
        LeaveScreen();
        WriteTo(_output, _errors, text, style);
    }

    public void WriteError(string text, OutputStyle style)
    {
        if (_errors.OnScreen)
        {
            LeaveScreen();
        }

        WriteTo(_errors, _output, text, style);
    }

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
            if (!_onScreen && _lastWritten is { AtLineStart: false } open)
            {
                WriteTo(open, open == _output ? _errors : _output, "\n", OutputStyle.None);
            }

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

        LeaveScreen();
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

        LeaveScreen();
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
        _written++;
        _output.AtLineStart = true;
        _lastWritten = _output;
        if (sharedScreen)
        {
            _errors.AtLineStart = true;
        }

        return true;
    }

    public bool TryShow(ViewNode view, OutputStyle style)
    {
        if (plain || richConsole is null || screen is null)
        {
            return false;
        }

        if (!_onScreen)
        {
            writer.Write(EnterAlternateScreen);
            _onScreen = true;
            _frame = null;
            _restoreOnCancel = (_, _) => RestoreScreen();
            Console.CancelKeyPress += _restoreOnCancel;
        }

        _shown = (view, style);
        Draw(screen);
        return true;
    }

    public bool TryWriteView(ViewNode view, OutputStyle style)
    {
        if (plain || richConsole is null)
        {
            return false;
        }

        LeaveScreen();
        EnsureLineStart();
        foreach (var line in ViewLayout.Page(view, style, richConsole.Profile.Width))
        {
            writer.Write(line.ToAnsi() + newLine);
        }

        writer.Flush();
        _written++;
        _output.AtLineStart = true;
        _lastWritten = _output;
        if (sharedScreen)
        {
            _errors.AtLineStart = true;
        }

        return true;
    }

    public bool TryReadKey(int? milliseconds, out string? key)
    {
        key = null;
        if (!keys || screen is null)
        {
            return false;
        }

        var watch = Stopwatch.StartNew();
        while (true)
        {
            while (screen.KeyAvailable)
            {
                var name = KeyNames.Of(screen.ReadKey());
                if (name is [var high] && char.IsHighSurrogate(high) && screen.KeyAvailable)
                {
                    name += screen.ReadKey().KeyChar;
                }

                if (name is not null)
                {
                    key = name;
                    return true;
                }
            }

            if (_onScreen && screen.Size != _frameSize)
            {
                Draw(screen);
            }

            var elapsed = watch.ElapsedMilliseconds;
            if (milliseconds is { } limit && elapsed >= limit)
            {
                return true;
            }

            screen.Wait(milliseconds is { } wait ? (int)Math.Min(KeyPollMilliseconds, wait - elapsed) : KeyPollMilliseconds);
        }
    }

    public void BeforeLineRead()
    {
        if (keys)
        {
            LeaveScreen();
        }
    }

    public void BeforeAttachedCommand()
    {
        LeaveScreen();
        writer.Flush();
    }

    public void LeaveScreen()
    {
        if (!_onScreen)
        {
            return;
        }

        RestoreScreen();
        _onScreen = false;
        _shown = null;
        _frame = null;
        if (_restoreOnCancel is not null)
        {
            Console.CancelKeyPress -= _restoreOnCancel;
            _restoreOnCancel = null;
        }
    }

    private void RestoreScreen()
    {
        writer.Write(LeaveAlternateScreen);
        writer.Flush();
    }

    private void Draw(ITerminalScreen terminal)
    {
        if (_shown is not { } shown)
        {
            return;
        }

        var size = terminal.Size;
        var builder = new StringBuilder();
        if (_frame is not null && size != _frameSize)
        {
            builder.Append("\e[2J");
            _frame = null;
        }

        var lines = ViewLayout.Screen(shown.View, shown.Style, size.Width, size.Height);
        var frame = new string[Math.Max(size.Height, 0)];
        for (var row = 0; row < frame.Length; row++)
        {
            frame[row] = row < lines.Count ? lines[row].ToAnsi() : "";
            if (_frame is null || _frame[row] != frame[row])
            {
                builder.Append("\e[").Append(row + 1).Append(";1H").Append(frame[row]).Append("\e[K");
            }
        }

        writer.Write(builder.ToString());
        writer.Flush();
        _frame = frame;
        _frameSize = size;
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
        LeaveScreen();
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

        _written++;

        _lastWritten = stream;
        if (sharedScreen)
        {
            other.AtLineStart = stream.AtLineStart;
        }
    }
}
