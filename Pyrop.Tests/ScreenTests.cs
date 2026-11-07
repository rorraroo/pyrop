using System.Text.RegularExpressions;
using Pyrop.Cli;
using Pyrop.Runtime;
using Spectre.Console;

namespace Pyrop.Tests;

public sealed class ScreenTests
{
    private const string Terminal = "use terminal library\nimport from terminal View show write_view read_key read_key_within end\n";
    private const string Enter = "\e[?1049h\e[?25l\e[2J";
    private const string Leave = "\e[?25h\e[?1049l";

    private sealed class FakeScreen(int width, int height) : ITerminalScreen
    {
        public Queue<ConsoleKeyInfo> Keys { get; } = new();

        public (int Width, int Height) Size { get; set; } = (width, height);

        public Action? OnWait { get; set; }

        public bool KeyAvailable => Keys.Count > 0;

        public ConsoleKeyInfo ReadKey() => Keys.Dequeue();

        public void Wait(int milliseconds)
        {
            OnWait?.Invoke();
            Thread.Sleep(Math.Max(milliseconds, 0));
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0', bool control = false, bool alt = false, bool shift = false) =>
        new(character, key, shift, alt, control);

    private static IAnsiConsole RichConsole(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes,
        });
        console.Profile.Width = 40;
        console.Profile.Height = 10;
        return console;
    }

    private static (string Text, ConsoleProgramOutput Output) RunRich(string body, FakeScreen screen, bool keys = true, string input = "")
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: RichConsole(writer), screen: screen, keys: keys);
        try
        {
            new PyropSession(new StringReader(input), output).Run(Terminal + body);
        }
        finally
        {
            output.LeaveScreen();
        }

        return (writer.ToString(), output);
    }

    private static List<string> Lines(IReadOnlyList<ScreenLine> lines) => lines.Select(line => line.PlainText).ToList();

    private static ViewNode Text(string text) => new TextView(text);

    [Fact]
    public void APanelDrawsABorderWithItsTitle()
    {
        var lines = Lines(ViewLayout.Screen(new PanelView("Hero", Text("ok")), OutputStyle.None, 21, 5));

        Assert.Equal(["╭─ Hero ───────────╮", "│ ok               │", "╰──────────────────╯"], lines);
    }

    [Fact]
    public void BarsFollowingEachOtherInRowsStartInTheSameColumn()
    {
        var rows = new StackView(false, [
            new BarView("Info", 1, 2),
            new StyledView(OutputStyle.Warning, new BarView("Warnings", 1, 2)),
            new BarView("Err", 1, 2),
            Text("x"),
            new BarView("Solo", 1, 2),
        ]);

        var lines = Lines(ViewLayout.Screen(rows, OutputStyle.None, 24, 5));

        Assert.Equal(["Info     ███░░░░ 1 of 2", "Warnings ███░░░░ 1 of 2", "Err      ███░░░░ 1 of 2", "x", "Solo █████░░░░░░ 1 of 2"], lines);
    }

    [Fact]
    public void ABarFillsInProportionAndShowsItsNumbers()
    {
        var line = Assert.Single(Lines(ViewLayout.Screen(new BarView("HP", 5, 10), OutputStyle.None, 23, 3)));

        Assert.Equal("HP █████░░░░░░ 5 of 10", line);
    }

    [Theory]
    [InlineData(-3, 20, "HP ░░░░░░░░░░ -3 of 20")]
    [InlineData(30, 20, "HP ██████████ 30 of 20")]
    [InlineData(0, 0, "HP ░░░░░░░░░░░░ 0 of 0")]
    public void ABarOutsideItsRangeIsEmptyOrFull(long value, long maximum, string expected)
    {
        Assert.Equal(expected, Assert.Single(Lines(ViewLayout.Screen(new BarView("HP", value, maximum), OutputStyle.None, 23, 3))));
    }

    [Fact]
    public void TextWrapsAtWordBoundaries()
    {
        var lines = Lines(ViewLayout.Screen(Text("the quick brown fox jumps over the dog"), OutputStyle.None, 20, 5));

        Assert.Equal(["the quick brown fox", "jumps over the dog"], lines);
    }

    [Fact]
    public void AMenuScrollsToItsSelection()
    {
        var entries = Enumerable.Range(0, 20).Select(index => $"item {index}").ToList();

        var lines = Lines(ViewLayout.Screen(new MenuView(entries, 10), OutputStyle.None, 20, 7));

        Assert.Equal(7, lines.Count);
        Assert.Equal("(8 more above)", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("› item 10"));
        Assert.Equal("(7 more below)", lines[^1]);
    }

    [Fact]
    public void AMenuThatFitsShowsEveryEntry()
    {
        var lines = Lines(ViewLayout.Screen(new MenuView(["lamp", "rope"], 1), OutputStyle.None, 20, 5));

        Assert.Equal("  lamp", lines[0]);
        Assert.StartsWith("› rope", lines[1]);
    }

    [Fact]
    public void ATableShowsTheRowsThatFitAndCountsTheRest()
    {
        var rows = Enumerable.Range(0, 10).Select(index => (IReadOnlyList<string>)[$"r{index}", "x"]).ToList();

        var lines = Lines(ViewLayout.Screen(new TableView(["Name", "V"], rows, []), OutputStyle.None, 20, 6));

        Assert.Equal(["Name  V", "───────", "r0    x", "r1    x", "r2    x", "(7 more rows)"], lines);
    }

    [Fact]
    public void AGridIsCroppedAroundItsFocus()
    {
        var map = Enumerable.Range(0, 10).Select(row => string.Concat(Enumerable.Range(0, 30).Select(column => (char)('a' + (row + column) % 26)))).ToList();

        var lines = Lines(ViewLayout.Screen(new GridView(map, 29, 9), OutputStyle.None, 21, 4));

        Assert.Equal(4, lines.Count);
        Assert.Equal(map[9][10..], lines[^1]);
        Assert.Equal(map[6][10..], lines[0]);
    }

    [Fact]
    public void ColumnsShareTheWidth()
    {
        var lines = Lines(ViewLayout.Screen(new StackView(true, [Text("left"), Text("right")]), OutputStyle.None, 21, 3));

        Assert.Equal(["left       right"], lines);
    }

    [Fact]
    public void RowsGiveTheRemainingHeightToMenus()
    {
        var entries = Enumerable.Range(0, 10).Select(index => $"e{index}").ToList();
        var view = new StackView(false, [Text("title"), new MenuView(entries, 0), new BarView("b", 1, 1)]);

        var lines = Lines(ViewLayout.Screen(view, OutputStyle.None, 20, 6));

        Assert.Equal(6, lines.Count);
        Assert.Equal("title", lines[0]);
        Assert.Equal("(7 more below)", lines[4]);
        Assert.StartsWith("b ", lines[5]);
    }

    [Theory]
    [InlineData(19, 10)]
    [InlineData(40, 2)]
    public void ATooSmallWindowAsksToBeLarger(int width, int height)
    {
        var lines = Lines(ViewLayout.Screen(Text("hi"), OutputStyle.None, width, height));

        Assert.StartsWith("Make the", lines[0]);
        Assert.True(lines.Count <= height || height < 1);
    }

    [Fact]
    public void ATerminalTwentyColumnsWideIsLargeEnough()
    {
        Assert.Equal(["hi"], Lines(ViewLayout.Screen(Text("hi"), OutputStyle.None, 20, 3)));
    }

    [Fact]
    public void TheTooSmallMessageWrapsInsteadOfBeingCut()
    {
        Assert.Equal(
            ["Make the window", "larger to see this", "screen."],
            Lines(ViewLayout.Screen(Text("hi"), OutputStyle.None, 19, 10)));
    }

    [Fact]
    public void AMenuSqueezedToNothingAsksForALargerWindow()
    {
        var view = new StackView(false, [Text("Title"), Text("Status"), Text("Help"), new MenuView(["lamp", "rope", "key"], 2)]);

        Assert.Equal([ViewLayout.TooSmall], Lines(ViewLayout.Screen(view, OutputStyle.None, 60, 3)));
        Assert.Contains("› key", string.Join("|", Lines(ViewLayout.Screen(view, OutputStyle.None, 60, 4))));
    }

    [Fact]
    public void ControlCharactersNeverReachTheScreen()
    {
        var view = new StackView(false, [new MenuView(["a\nb", "c\td"], 0), new PanelView("t\ri", Text("x\ty\r\nz")), new GridView(["#\u0007#"], 0, 0)]);

        var lines = Lines(ViewLayout.Screen(view, OutputStyle.None, 30, 12));

        Assert.All(lines, line => Assert.DoesNotContain(line, char.IsControl));
        Assert.Contains(lines, line => line.StartsWith("› a b"));
        Assert.Contains(lines, line => line.Contains("x y"));
        Assert.Contains(lines, line => line.Contains('z'));
    }

    [Fact]
    public void NoLineIsWiderThanTheScreen()
    {
        var random = new Random(7);
        for (var round = 0; round < 2000; round++)
        {
            var view = RandomView(random, 4);
            var width = random.Next(-2, 60);
            var height = random.Next(-2, 30);

            var lines = ViewLayout.Screen(view, OutputStyle.None, width, height);

            Assert.All(lines, line => Assert.True(line.Width <= Math.Max(width - 1, 1), $"{line.Width} > {width - 1}: {view}"));
            Assert.True(lines.Count <= Math.Max(height, 1));
        }
    }

    [Fact]
    public void ALongWordAndALargeTableLayOutQuickly()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var rows = Enumerable.Range(0, 20000).Select(index => (IReadOnlyList<string>)[$"row {index}", "x"]).ToList();

        ViewLayout.Screen(new StackView(false, [Text(new string('a', 100000)), new TableView(["a", "b"], rows, [])]), OutputStyle.None, 80, 30);
        ViewLayout.Page(new TableView(["a", "b"], rows, []), OutputStyle.None, 80);

        Assert.True(watch.ElapsedMilliseconds < 10000, $"{watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ReadLeavesTheScreenWhenTypingOnTheTerminal()
    {
        var (text, output) = RunRich("show text \"a\"\ndef string s read\nwrite_line s", new FakeScreen(30, 5), input: "typed\n");

        Assert.False(output.OnAlternateScreen);
        Assert.True(text.IndexOf(Leave, StringComparison.Ordinal) < text.IndexOf("typed", StringComparison.Ordinal));
    }

    private static ViewNode RandomView(Random random, int depth)
    {
        string Word() => new('w', random.Next(0, 40));
        return (depth <= 0 ? random.Next(5) : random.Next(9)) switch
        {
            0 => Text(string.Join(" ", Enumerable.Range(0, random.Next(0, 6)).Select(_ => Word()))),
            1 => new BarView(Word(), random.NextInt64(-5, 50), random.NextInt64(-5, 50)),
            2 => new MenuView(Enumerable.Range(0, random.Next(0, 30)).Select(_ => Word()).ToList(), random.Next(-2, 32)),
            3 => new GridView(Enumerable.Range(0, random.Next(0, 30)).Select(_ => Word()).ToList(), random.Next(-5, 50), random.Next(-5, 50)),
            4 => new TableView(
                Enumerable.Range(0, random.Next(1, 12)).Select(_ => Word()).ToList() is var headers ? headers : [],
                Enumerable.Range(0, random.Next(0, 10)).Select(_ => (IReadOnlyList<string>)headers.Select(_ => Word()).ToList()).ToList(),
                []),
            5 => new PanelView(Word(), RandomView(random, depth - 1)),
            6 => new StackView(false, Enumerable.Range(0, random.Next(0, 4)).Select(_ => RandomView(random, depth - 1)).ToList()),
            7 => new StackView(true, Enumerable.Range(0, random.Next(0, 31)).Select(_ => RandomView(random, depth - 1)).ToList()),
            _ => new StyledView(OutputStyle.Danger, RandomView(random, depth - 1)),
        };
    }

    [Fact]
    public void FixedPartsThatDoNotFitAskToBeLarger()
    {
        var view = new StackView(false, [Text("one"), Text("two"), Text("three"), Text("four")]);

        Assert.Equal([ViewLayout.TooSmall], Lines(ViewLayout.Screen(view, OutputStyle.None, 60, 3)));
    }

    [Fact]
    public void InStyleColoursItsPart()
    {
        var line = Assert.Single(ViewLayout.Screen(new StyledView(OutputStyle.Danger, Text("Poisoned")), OutputStyle.None, 20, 3));

        Assert.Equal("\e[31mPoisoned\e[0m", line.ToAnsi());
    }

    [Fact]
    public void ShowUsesTheAlternateScreenAndLeavesItForOtherOutput()
    {
        var (text, _) = RunRich("show text \"hello\"\nwrite_line \"after\"", new FakeScreen(30, 5));

        Assert.StartsWith(Enter, text);
        Assert.Contains("\e[1;1Hhello\e[K", text);
        var leave = text.IndexOf(Leave, StringComparison.Ordinal);
        Assert.True(leave > 0);
        Assert.True(text.IndexOf("after", StringComparison.Ordinal) > leave);
    }

    [Fact]
    public void ShowRedrawsOnlyTheLinesThatChanged()
    {
        var (text, _) = RunRich(
            "show rows list_of View text \"same\" text \"one\" end\nshow rows list_of View text \"same\" text \"two\" end",
            new FakeScreen(30, 5));

        Assert.Single(Regex.Matches(text, Regex.Escape("\e[1;1Hsame")));
        Assert.Contains("\e[2;1Htwo\e[K", text);
    }

    [Fact]
    public void TheScreenIsLeftWhenTheProgramFails()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false, richConsole: RichConsole(writer), screen: new FakeScreen(30, 5));
        var session = new PyropSession(new StringReader(""), output);

        Assert.Throws<PyropException>(() => session.Run(Terminal + "show text \"x\"\nwrite_line divide 1 0"));
        Assert.True(output.OnAlternateScreen);
        output.BeginErrorMessage();

        Assert.False(output.OnAlternateScreen);
        Assert.EndsWith(Leave, writer.ToString());
    }

    [Fact]
    public void AColorIsTheTerminalsOwnForegroundColor()
    {
        var line = Assert.Single(ViewLayout.Screen(new StyledView(OutputStyle.BrightBlue, Text("water")), OutputStyle.None, 20, 3));

        Assert.Equal("\e[94mwater\e[0m", line.ToAnsi());
    }

    [Fact]
    public void AColoredGridColorsItsCharactersAndLeavesTheRestInTheSurroundingStyle()
    {
        var colors = new Dictionary<string, OutputStyle> { ["#"] = OutputStyle.Grey, ["g"] = OutputStyle.Green };
        var grid = new GridView(["##g.@"], 0, 0, colors);

        var line = Assert.Single(ViewLayout.Screen(grid, OutputStyle.Muted, 20, 3));

        Assert.Equal("\e[90m##\e[0m\e[32mg\e[0m\e[90m.@\e[0m", line.ToAnsi());
    }

    [Fact]
    public void AColoredGridIsCroppedLikeAGrid()
    {
        var map = Enumerable.Range(0, 10).Select(row => new string('#', 30) + "g").ToList();
        var colors = new Dictionary<string, OutputStyle> { ["g"] = OutputStyle.Green };

        var plain = Lines(ViewLayout.Screen(new GridView(map, 29, 9), OutputStyle.None, 21, 4));
        var colored = Lines(ViewLayout.Screen(new GridView(map, 29, 9, colors), OutputStyle.None, 21, 4));

        Assert.Equal(plain, colored);
    }

    [Fact]
    public void WriteViewDrawsAColoredMapInColor()
    {
        var (text, _) = RunRich(
            "write_view colored_grid list_of string \"#g#\" end map_of string string \"g\" \"green\" \"#\" \"grey\" end 0 0",
            new FakeScreen(30, 5));

        Assert.Contains("\e[90m#\e[0m\e[32mg\e[0m\e[90m#\e[0m", text);
    }

    [Fact]
    public void AColorInsideAStyledPanelKeepsItsColor()
    {
        var view = new StyledView(OutputStyle.Danger, new PanelView("P", new StyledView(OutputStyle.Cyan, Text("x"))));

        var lines = ViewLayout.Screen(view, OutputStyle.None, 20, 3);

        Assert.Contains(lines[1].Segments, segment => segment.Text == "x" && segment.Style == OutputStyle.Cyan);
        Assert.All(lines[1].Segments.Where(segment => segment.Text.Contains('│')), segment => Assert.Equal(OutputStyle.Danger, segment.Style));
    }

    [Fact]
    public void WriteViewDrawsIntoOrdinaryOutput()
    {
        var (text, _) = RunRich("write \"a\"\nwrite_view panel \"P\" text \"x\"", new FakeScreen(30, 5));

        Assert.DoesNotContain("\e[?1049h", text);
        var visible = Regex.Replace(text, "\e\\[[0-9;]*m", "");
        Assert.Contains("a\n╭─ P", visible);
        Assert.Contains("│ x", visible);
    }

    [Fact]
    public void ReadKeyReadsRealKeys()
    {
        var screen = new FakeScreen(30, 5);
        foreach (var key in new[] { Key(ConsoleKey.DownArrow), Key(ConsoleKey.A, 'a', alt: true), Key(ConsoleKey.Q, 'q') })
        {
            screen.Keys.Enqueue(key);
        }

        var (text, _) = RunRich("write_line read_key\nwrite_line read_key", screen);

        Assert.Equal("down\nq\n", text);
    }

    [Fact]
    public void ReadKeyWithinTimesOutWithoutAKey()
    {
        var (text, _) = RunRich("write_line read_key_within 30\nwrite_line read_key_within 0", new FakeScreen(30, 5));

        Assert.Equal("none\nnone\n", text);
    }

    [Fact]
    public void APipedLineReadAheadIsUsedBeforeRealKeys()
    {
        var screen = new FakeScreen(30, 5);
        screen.Keys.Enqueue(Key(ConsoleKey.X, 'x'));

        var (text, _) = RunRich("if not input_ended write_line read_key\nwrite_line read_key", screen, input: "typed\n");

        Assert.Equal("typed\nx\n", text);
    }

    [Fact]
    public void AResizeWhileWaitingRedrawsTheScreen()
    {
        var screen = new FakeScreen(30, 5);
        screen.OnWait = () => screen.Size = (40, 6);

        var (text, _) = RunRich("show text \"hello\"\ndef option string key read_key_within 50", screen);

        Assert.Equal(2, Regex.Matches(text, Regex.Escape("\e[1;1Hhello")).Count);
        Assert.Contains("\e[2J\e[1;1Hhello", text);
    }

    [Fact]
    public void WithoutKeysReadKeyFallsBackToLines()
    {
        var (text, _) = RunRich("write_line read_key", new FakeScreen(30, 5), keys: false, input: "left\n");

        Assert.Equal("left\n", text);
    }

    [Theory]
    [InlineData(ConsoleKey.UpArrow, '\0', false, false, "up")]
    [InlineData(ConsoleKey.PageDown, '\0', false, false, "page_down")]
    [InlineData(ConsoleKey.F12, '\0', false, false, "f12")]
    [InlineData(ConsoleKey.Spacebar, ' ', false, false, "space")]
    [InlineData(ConsoleKey.Enter, '\r', false, false, "enter")]
    [InlineData(ConsoleKey.S, '\u0013', true, false, "control_s")]
    [InlineData(ConsoleKey.A, 'A', false, false, "A")]
    [InlineData(ConsoleKey.Oem2, '?', false, false, "?")]
    [InlineData(ConsoleKey.C, 'č', false, false, "č")]
    [InlineData(ConsoleKey.Q, '@', true, true, "@")]
    [InlineData(ConsoleKey.A, 'a', false, true, null)]
    [InlineData(ConsoleKey.UpArrow, '\0', true, false, null)]
    [InlineData(ConsoleKey.VolumeUp, '\0', false, false, null)]
    public void KeysHaveNames(ConsoleKey key, char character, bool control, bool alt, string? expected)
    {
        Assert.Equal(expected, KeyNames.Of(Key(key, character, control, alt)));
    }
}
