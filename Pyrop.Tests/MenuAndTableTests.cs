using Pyrop.Cli;
using Pyrop.Runtime;
using Pyrop.Syntax;
using Spectre.Console;

namespace Pyrop.Tests;

public class MenuAndTableTests
{
    private const string Rooms = "list_of string \"Hall\" \"Cellar\" \"Tower\" end";

    [Fact]
    public void ChooseWritesANumberedListAndReturnsTheChosenEntry()
    {
        var output = Run.Program($"def string room choose \"Where do you go?\" {Rooms}\nwrite_line add \"Going to \" room", "2\n");

        Assert.Equal("Where do you go?\n1. Hall\n2. Cellar\n3. Tower\nChoose a number from 1 to 3: Going to Cellar\n", output);
    }

    [Fact]
    public void ChooseRemovesTrailingWhitespaceFromThePrompt()
    {
        Assert.StartsWith("Pick\n1. Hall\n", Run.Program($"write_line choose \"Pick  \\n\" {Rooms}", "1\n"));
    }

    [Fact]
    public void ChooseAlignsTheNumbers()
    {
        const string source = "write_line choose \"Pick\" list_of string \"a\" \"b\" \"c\" \"d\" \"e\" \"f\" \"g\" \"h\" \"i\" \"j\" end";

        Assert.Equal(
            "Pick\n 1. a\n 2. b\n 3. c\n 4. d\n 5. e\n 6. f\n 7. g\n 8. h\n 9. i\n10. j\nChoose a number from 1 to 10: j\n",
            Run.Program(source, "10\n"));
    }

    [Fact]
    public void ChooseAsksAgainAfterAnInvalidAnswerWithoutRepeatingTheList()
    {
        var output = Run.Program($"write_line choose \"Pick\" {Rooms}", "\nabc\n0\n4\n-1\n+1\n1.0\n٣\n99999999999999999999\n 03 \n");

        var retry = "<warning>Please choose a number from 1 to 3.\n</warning>Choose a number from 1 to 3: ";
        Assert.Equal(
            "Pick\n1. Hall\n2. Cellar\n3. Tower\nChoose a number from 1 to 3: " + string.Concat(Enumerable.Repeat(retry, 9)) + "Tower\n",
            output);
    }

    [Fact]
    public void ChooseAllowsDuplicateEntries()
    {
        Assert.EndsWith(": same\n", Run.Program("write_line choose \"Pick\" list_of string \"same\" \"same\" end", "2\n"));
    }

    [Fact]
    public void ChooseAcceptsALineReadAheadByInputEnded()
    {
        Assert.EndsWith(": Hall\n", Run.Program($"if not input_ended write_line choose \"Pick\" {Rooms}", "1\n"));
    }

    [Fact]
    public void ChooseAppliesTheStyleToEverythingButTheWarning()
    {
        var output = Run.Program($"with_style \"info\" write_line choose \"Pick\" list_of string \"a\" end", "x\n1\n");

        Assert.Equal(
            "<info>Pick\n</info><info>1. a\n</info><info>Choose number 1: </info>" +
            "<warning>Please choose number 1.\n</warning><info>Choose number 1: </info><info>a\n</info>",
            output);
    }

    [Theory]
    [InlineData("write_line choose \"Pick\" empty list string", "", "'choose' needs at least one entry.")]
    [InlineData("write_line choose \"Pick\" list_of string \"a\" end", "", "'choose' reached the end of input.")]
    [InlineData("write_line choose \"Pick\" list_of string \"a\" end", "x\n", "'choose' reached the end of input.")]
    public void ChooseFailsAtRuntime(string source, string input, string message)
    {
        var error = Run.Failure(source, input);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void AnEmptyChooseWritesNothing()
    {
        var output = new StringOutput();
        Assert.Throws<PyropException>(() => new PyropSession(new StringReader("1\n"), output).Run("write choose \"Pick\" empty list string"));

        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void AttemptedChooseIsAbsentForAnEmptyListOrAtEndOfInput()
    {
        const string source =
            "write_line attempt choose \"a\" empty list string\n" +
            "write_line attempt choose \"b\" list_of string \"x\" end\n" +
            "write_line attempt choose \"c\" list_of string \"y\" end";

        Assert.Equal("none\nb\n1. x\nChoose number 1: some \"x\"\nc\n1. y\nChoose number 1: none\n", Run.Program(source, "1\n"));
    }

    [Fact]
    public void ChooseChecksItsOperandTypes()
    {
        var error = Run.Failure("write_line choose \"Pick\" list_of int 1 end");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'choose' cannot take string list int. It accepts: string list string.", error.Message);
    }

    [Fact]
    public void ChooseAndWriteTableAreReserved()
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure("def string choose \"x\"").Stage);
        Assert.Equal(ErrorStage.Parse, Run.Failure("def int write_table 1").Stage);
    }

    [Fact]
    public void WriteTablePadsColumnsWithSpaces()
    {
        const string source =
            "write_table list_of string \"Item\" \"Count\" \"Where\" end list_of list string\n" +
            "    list_of string \"lamp\" \"1\" \"hall\" end\n" +
            "    list_of string \"rope\" \"12\" \"\" end\n" +
            "end";

        Assert.Equal("Item  Count  Where\nlamp  1      hall\nrope  12     \n", Run.Program(source));
    }

    [Fact]
    public void WriteTableCountsCharactersNotUtf16Units()
    {
        const string source =
            "write_table list_of string \"Name\" \"Hp\" end list_of list string\n" +
            "    list_of string \"čau 🗡️\" \"3\" end\n" +
            "    list_of string \"ab\" \"4\" end\n" +
            "end";

        Assert.Equal("Name   Hp\nc\u030cau 🗡️  3\nab     4\n", Run.Program(source));
    }

    [Fact]
    public void ATableAndANumberedListStartOnANewLine()
    {
        const string source =
            "write \"partial\"\nwrite_table list_of string \"a\" end empty list list string\n" +
            "write \"more\"\nwrite_line choose \"Pick\" list_of string \"x\" \"y\" end\n" +
            "write_line \"done\"\nwrite_table list_of string \"b\" end empty list list string";

        Assert.Equal("partial\na\nmore\nPick\n1. x\n2. y\nChoose a number from 1 to 2: y\ndone\nb\n", Run.Program(source, "2\n"));
    }

    [Fact]
    public void ATableAfterAPipedAnswerStartsOnTheNextLine()
    {
        const string source =
            "def string room choose \"Where do you go?\" list_of string \"Hall\" \"Cellar\" \"Tower\" end\n" +
            "write_table list_of string \"Item\" \"Count\" end list_of list string\n" +
            "    list_of string \"lamp\" \"1\" end\n" +
            "    list_of string \"rope\" \"12\" end\n" +
            "end";

        Assert.Equal(
            "Where do you go?\n1. Hall\n2. Cellar\n3. Tower\nChoose a number from 1 to 3: \nItem  Count\nlamp  1\nrope  12\n",
            Run.Program(source, "2\n"));
    }

    [Fact]
    public void WriteTableWithoutRowsWritesTheHeaders()
    {
        Assert.Equal("a  b\n", Run.Program("write_table list_of string \"a\" \"b\" end empty list list string"));
    }

    [Fact]
    public void WriteTableAppliesTheStyle()
    {
        const string source = "with_style \"muted\" write_table list_of string \"a\" end list_of list string list_of string \"b\" end end";

        Assert.Equal("<muted>a\n</muted><muted>b\n</muted>", Run.Program(source));
    }

    [Theory]
    [InlineData(
        "list_of string \"a\" \"b\" end list_of list string list_of string \"1\" \"2\" end list_of string \"1\" \"2\" \"3\" end end",
        "Row 1 of the table has 3 cells, but the table has 2 headers; rows are numbered from 0.")]
    [InlineData(
        "list_of string \"a\" end list_of list string list_of string end end",
        "Row 0 of the table has 0 cells, but the table has 1 header; rows are numbered from 0.")]
    [InlineData(
        "list_of string \"a\" \"b\" end list_of list string list_of string \"1\" end end",
        "Row 0 of the table has 1 cell, but the table has 2 headers; rows are numbered from 0.")]
    [InlineData("empty list string empty list list string", "'write_table' needs at least one header.")]
    public void WriteTableFailsBeforeWritingAnything(string operands, string message)
    {
        var output = new StringOutput();
        var exception = Assert.Throws<PyropException>(() => new PyropSession(new StringReader(""), output).Run($"write_table {operands}"));

        var error = Assert.Single(exception.Errors);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void WriteTableRejectsALineThatWouldBeTooLong()
    {
        const string source =
            "def string wide repeat \"a\" 50000001\n" +
            "write_table list_of string \"x\" \"y\" end list_of list string list_of string wide wide end end";

        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("The table of 'write_table' would exceed the largest allowed text size.", error.Message);
    }

    [Fact]
    public void AttemptedWriteTableGivesFalseAndWritesNothing()
    {
        const string source =
            "write_line attempt write_table list_of string \"a\" end list_of list string list_of string end end\n" +
            "write_line attempt write_table list_of string \"a\" end empty list list string";

        Assert.Equal("false\na\ntrue\n", Run.Program(source));
    }

    [Fact]
    public void WriteTableIsAStatement()
    {
        var error = Run.Failure("write_line write_table list_of string \"a\" end empty list list string");

        Assert.Equal(ErrorStage.Parse, error.Stage);
    }

    [Fact]
    public void WriteTableChecksItsOperandTypes()
    {
        var error = Run.Failure("write_table list_of string \"a\" end list_of string \"b\" end");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'write_table' cannot take list string list string. It accepts: list string list list string.", error.Message);
    }

    [Fact]
    public void TreeBracketsChooseAndWriteTableOperands()
    {
        var source = "def string r choose \"Go?\" list_of string \"a\" end\nwrite_table list_of string \"h\" end empty list list string";
        var inspected = new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, "main.prp");

        Assert.Null(inspected.Error);
        Assert.Equal(
            "def string r (choose \"Go?\" (list_of string \"a\" end))\nwrite_table (list_of string \"h\" end) (empty list list string)\n",
            TreePrinter.Print(inspected.Tree!));
    }

    [Fact]
    public void ChooseUsesARichPickWhenTheOutputOffersOne()
    {
        var output = new PickingOutput(1);

        var exitCode = new PyropSession(new StringReader(""), output).Run($"write_line choose \"Pick\" {Rooms}");

        Assert.Null(exitCode);
        Assert.Equal("Cellar\n", output.Text.ToString());
        Assert.Equal(["Hall", "Cellar", "Tower"], output.Offered);
    }

    [Fact]
    public void ChooseUsesTheNumberedFormWhileALineIsReadAhead()
    {
        var output = new PickingOutput(1);

        new PyropSession(new StringReader("3\n"), output).Run($"if not input_ended write_line choose \"Pick\" {Rooms}");

        Assert.Equal("Pick\n1. Hall\n2. Cellar\n3. Tower\nChoose a number from 1 to 3: Tower\n", output.Text.ToString());
        Assert.Null(output.Offered);
    }

    [Fact]
    public void WriteTableLeavesARichTableToTheOutput()
    {
        var output = new PickingOutput(0);

        new PyropSession(new StringReader(""), output).Run("write_table list_of string \"a\" end list_of list string list_of string \"b\" end end");

        Assert.Equal("", output.Text.ToString());
        Assert.Equal("a|b", output.Table);
    }

    [Fact]
    public void ConsoleOutputDeclinesATableThatCannotFitTheTerminal()
    {
        var writer = new StringWriter();
        var console = RichConsole(writer);
        console.Profile.Width = 40;
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: console);
        var headers = Enumerable.Range(0, 12).Select(index => $"h{index}").ToList();

        Assert.False(output.TryWriteTable(headers, [headers], OutputStyle.None));
        Assert.False(output.TryWriteTable(["a", "b"], [["a-very-long-unbreakable-word-here", "another-long-unbreakable-word"]], OutputStyle.None));
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void ConsoleOutputWrapsATableWhoseWordsFit()
    {
        var writer = new StringWriter();
        var console = RichConsole(writer);
        console.Profile.Width = 30;
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: console);

        Assert.True(output.TryWriteTable(["Item", "Note"], [["lamp", "an old brass lamp that still works fine"]], OutputStyle.None));

        var text = writer.ToString();
        Assert.All(["Item", "Note", "lamp", "old", "brass", "still", "works", "fine"], word => Assert.Contains(word, text));
        var visible = System.Text.RegularExpressions.Regex.Replace(text, "\e\\[[0-9;]*m", "");
        Assert.All(visible.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries), line => Assert.True(line.Length <= 30));
    }

    [Fact]
    public void ConsoleOutputRendersATableWithPlainTextCells()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: RichConsole(writer));

        output.Write("before", OutputStyle.None);
        Assert.True(output.TryWriteTable(["Item", "Count"], [["[red]lamp", "1"]], OutputStyle.None));

        var text = writer.ToString().Replace("\r\n", "\n");
        Assert.StartsWith("before\n┌", text);
        Assert.Contains("│ [red]lamp │ 1     │", text);
        Assert.True(output.AtLineStart);
    }

    [Fact]
    public void PlainConsoleOutputDeclinesTheRichForms()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: true, richConsole: RichConsole(writer), selectable: true);

        Assert.False(output.TryWriteTable(["a"], [], OutputStyle.None));
        Assert.Null(output.TryChoose("Pick", ["a"], OutputStyle.None));
        Assert.Equal("", writer.ToString());
    }

    [Fact]
    public void ConsoleOutputDeclinesTheRichChooseWhenNotSelectable()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: RichConsole(writer));

        Assert.Null(output.TryChoose("Pick", ["a"], OutputStyle.None));
    }

    [Fact]
    public void ConsoleOutputPicksWithArrowKeysAndLeavesALinearRecord()
    {
        var writer = new StringWriter();
        var screen = new StringWriter();
        var console = new ScriptedConsole(RichConsole(screen), ConsoleKey.DownArrow, ConsoleKey.DownArrow, ConsoleKey.UpArrow, ConsoleKey.Enter);
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: console, selectable: true);

        var picked = output.TryChoose("Where?  ", ["Hall", "[bold]Cellar", "Tower"], OutputStyle.None);

        Assert.Equal(1, picked);
        Assert.Equal("\e[36m? \e[0mWhere? [bold]Cellar\n", writer.ToString());
        Assert.Contains("[bold]Cellar", screen.ToString());
        Assert.True(output.AtLineStart);
    }

    private static IAnsiConsole RichConsole(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 80;
        console.Profile.Height = 24;
        return console;
    }

    private sealed class ScriptedConsole(IAnsiConsole inner, params ConsoleKey[] keys) : IAnsiConsole, IAnsiConsoleInput
    {
        private readonly Queue<ConsoleKey> _keys = new(keys);

        public Profile Profile => inner.Profile;

        public IAnsiConsoleCursor Cursor => inner.Cursor;

        public IAnsiConsoleInput Input => this;

        public IExclusivityMode ExclusivityMode => inner.ExclusivityMode;

        public Spectre.Console.Rendering.RenderPipeline Pipeline => inner.Pipeline;

        public void Clear(bool home) => inner.Clear(home);

        public void Write(Spectre.Console.Rendering.IRenderable renderable) => inner.Write(renderable);

        public void WriteAnsi(Action<AnsiWriter> action) => inner.WriteAnsi(action);

        public bool IsKeyAvailable() => _keys.Count > 0;

        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            var key = _keys.Dequeue();
            return new ConsoleKeyInfo(key == ConsoleKey.Enter ? '\r' : '\0', key, false, false, false);
        }

        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
    }

    private sealed class PickingOutput(int pick) : IProgramOutput
    {
        public System.Text.StringBuilder Text { get; } = new();

        public IReadOnlyList<string>? Offered { get; private set; }

        public string? Table { get; private set; }

        public void Write(string text, OutputStyle style) => Text.Append(text);

        public void WriteError(string text, OutputStyle style)
        {
        }

        public void WritePrompt(string prompt, string? hint, OutputStyle style) => Text.Append(prompt).Append(hint);

        public void LineRead()
        {
        }

        public void EnsureLineStart()
        {
        }

        public int? TryChoose(string prompt, IReadOnlyList<string> entries, OutputStyle style)
        {
            Offered = entries;
            return pick;
        }

        public bool TryWriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, OutputStyle style)
        {
            Table = string.Join("|", headers.Concat(rows.SelectMany(row => row)));
            return true;
        }
    }
}
