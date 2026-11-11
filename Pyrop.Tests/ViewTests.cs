namespace Pyrop.Tests;

public sealed class ViewTests
{
    private const string Terminal = "use terminal library\nimport from terminal View text table menu bar panel rows columns grid in_style in_color colored_grid show write_view read_key read_key_within end\n";

    private static string Views(string body, string input = "") => Run.Program(Terminal + body, input);

    private static PyropError ViewFailure(string body, string input = "") => Run.Failure(Terminal + body, input);

    [Fact]
    public void TextIsWrittenAsItIs()
    {
        Assert.Equal("Hello\nworld\n", Views("write_view text \"Hello\\nworld\""));
    }

    [Fact]
    public void ATableIsWrittenInWriteTablesPlainForm()
    {
        Assert.Equal(
            "Name  Count\nlamp  1\n",
            Views("write_view table list_of string \"Name\" \"Count\" end list_of list string list_of string \"lamp\" \"1\" end end"));
    }

    [Fact]
    public void AMenuMarksItsSelectedEntry()
    {
        Assert.Equal("lamp\nrope (selected)\n", Views("write_view menu list_of string \"lamp\" \"rope\" end 1"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AMenuPositionWithoutAnEntrySelectsNothing(int position)
    {
        Assert.Equal("lamp\nrope\n", Views($"write_view menu list_of string \"lamp\" \"rope\" end {position}"));
    }

    [Fact]
    public void AnEmptyMenuWritesNothing()
    {
        Assert.Equal("", Views("write_view menu list_of string end 0"));
    }

    [Theory]
    [InlineData("bar \"Health\" 7 20", "Health: 7 of 20\n")]
    [InlineData("bar \"Health\" -3 20", "Health: -3 of 20\n")]
    [InlineData("bar \"Lines\" 0 0", "Lines: 0 of 0\n")]
    public void ABarShowsItsNumbersAsGiven(string view, string expected)
    {
        Assert.Equal(expected, Views($"write_view {view}"));
    }

    [Fact]
    public void APanelWritesItsTitleAndThenItsContents()
    {
        Assert.Equal("Hero\nHealth: 7 of 20\nPoisoned\n", Views(
            "write_view panel \"Hero\" rows list_of View bar \"Health\" 7 20 text \"Poisoned\" end"));
    }

    [Fact]
    public void ColumnsWriteTheirViewsOneAfterAnother()
    {
        Assert.Equal("left\nright\n", Views("write_view columns list_of View text \"left\" text \"right\" end"));
    }

    [Fact]
    public void EmptyRowsWriteNothing()
    {
        Assert.Equal("", Views("write_view rows list_of View end"));
    }

    [Fact]
    public void AGridWritesItsLinesWithoutPadding()
    {
        Assert.Equal("#..#\n#@\n", Views("write_view grid list_of string \"#..#\" \"#@\" end 9 9"));
    }

    [Fact]
    public void InStyleGivesAPartItsOwnStyleAndTheInnermostWins()
    {
        Assert.Equal(
            "<warning>Hero\n</warning><danger>Poisoned\n</danger><warning>ok\n</warning>",
            Views("styled \"warning\" write_view panel \"Hero\" rows list_of View in_style \"danger\" text \"Poisoned\" text \"ok\" end"));
    }

    [Fact]
    public void AComputedStyleIsUsed()
    {
        Assert.Equal("<success>done\n</success>", Views("define string mood \"success\"\nwrite_view in_style mood text \"done\""));
    }

    [Fact]
    public void WriteViewStartsOnALineOfItsOwn()
    {
        Assert.Equal("Loading\nHi\n", Views("write \"Loading\"\nwrite_view text \"Hi\""));
    }

    [Fact]
    public void ShowWritesTheViewAndAnEmptyLineInPlainMode()
    {
        Assert.Equal("one\n\ntwo\n\n", Views("show text \"one\"\nshow text \"two\""));
    }

    [Fact]
    public void AViewsTextFormShowsHowItWasMade()
    {
        Assert.Equal(
            "panel \"Hero\" (rows [bar \"Health\" 7 20, in_style \"danger\" (text \"Poisoned\")])\n",
            Views("write_line panel \"Hero\" rows list_of View bar \"Health\" 7 20 in_style \"danger\" text \"Poisoned\" end"));
    }

    [Fact]
    public void ViewsAreComparedAndTakenApartLikeAnyChoice()
    {
        Assert.Equal("true\nbar Health\n", Views(
            "define View health bar \"Health\" 7 20\n" +
            "write_line equal health bar \"Health\" 7 20\n" +
            "match health\n" +
            "    when bar label value maximum write_line add \"bar \" label\n" +
            "    when text t write_line t\n" +
            "    when table h r write_line \"table\"\n" +
            "    when menu e p write_line \"menu\"\n" +
            "    when panel t v write_line \"panel\"\n" +
            "    when rows v write_line \"rows\"\n" +
            "    when columns v write_line \"columns\"\n" +
            "    when grid l c r write_line \"grid\"\n" +
            "    when in_style s v write_line \"in_style\"\n" +
            "    when in_color s v write_line \"in_color\"\n" +
            "    when colored_grid l m c r write_line \"colored_grid\"\n" +
            "end"));
    }

    [Fact]
    public void ViewsWorkQualifiedWithoutAnImport()
    {
        Assert.Equal("x\n", Run.Program(
            "use terminal library\ndefine from terminal View v from terminal text \"x\"\nfrom terminal write_view v"));
    }

    [Fact]
    public void SomeVariantsOfViewCanBeImportedKeepingTheOtherWordsFree()
    {
        Assert.Equal("Hero\nHealth: 7 of 20\nPoisoned\n", Run.Program(
            "use terminal library\nimport from terminal View panel rows bar write_view end\n" +
            "function View status string text return panel \"Hero\" rows list_of View bar \"Health\" 7 20 from terminal text text end\n" +
            "write_view status \"Poisoned\""));
    }

    private const string Panels =
        "use terminal library\nimport from terminal View show text rows panel end\n" +
        "function View screen int a int b return rows list_of View panel \"A\" text convert string a panel \"B\" text convert string b text \"keys\" end\n";

    [Fact]
    public void APlainShowLeavesOutPanelsThatDidNotChange()
    {
        Assert.Equal(
            "A\n1\nB\n1\nkeys\n\nB\n2\nkeys\n\nkeys\n\nA\n3\nkeys\n\n",
            Run.Program(Panels + "show screen 1 1\nshow screen 1 2\nshow screen 1 2\nshow screen 3 2"));
    }

    [Fact]
    public void OutputBetweenShowsOrOtherPanelsWriteTheWholeScreen()
    {
        Assert.Equal(
            "A\n1\nB\n1\nkeys\n\nbetween\nA\n1\nB\n1\nkeys\n\nA\n1\nkeys\n\nA\n1\nB\n1\nkeys\n\n",
            Run.Program(Panels +
                "show screen 1 1\nwrite_line \"between\"\nshow screen 1 1\n" +
                "show rows list_of View panel \"A\" text \"1\" text \"keys\" end\nshow screen 1 1"));
    }

    [Fact]
    public void AChangeOfStyleAloneLeavesAPlainPanelOut()
    {
        Assert.Equal(
            "<info>A\n</info><info>1\n</info>keys\n\nkeys\n\n",
            Run.Program(
                "use terminal library\nimport from terminal View show text rows panel in_style end\n" +
                "function View screen string style return rows list_of View in_style style panel \"A\" text \"1\" text \"keys\" end\n" +
                "show screen \"info\"\nwrite \"\"\nshow screen \"danger\""));
    }

    [Fact]
    public void EachRunStartsWithAWholeScreen()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);
        session.Run("use terminal library\nimport from terminal show panel text end");

        session.Run("show panel \"A\" text \"a\"");
        session.Run("show panel \"A\" text \"a\"");

        Assert.Equal("A\na\n\nA\na\n\n", output.ToString());
    }

    [Fact]
    public void TheHintForAVariantOfAPartlyImportedViewNamesTheVariant()
    {
        Assert.Equal(
            "'text' comes from the library terminal: add import from terminal text end, or write from terminal text.",
            Run.Failure("use terminal library\nimport from terminal View panel end\nwrite_line text").Message);
    }

    [Fact]
    public void WriteViewAlwaysWritesTheWholeView()
    {
        Assert.Equal(
            "A\n1\nB\n1\nkeys\nA\n1\nB\n1\nkeys\n",
            Run.Program(Panels.Replace("show text", "write_view text") + "write_view screen 1 1\nwrite_view screen 1 1"));
    }

    [Fact]
    public void AnUnimportedVariantGetsTheLibraryHint()
    {
        Assert.Equal(
            "'panel' comes from the library terminal: add use terminal library and import from terminal panel end, or write from terminal panel.",
            Run.Failure("write_line panel").Message);
    }

    [Fact]
    public void ViewNamesStayFreeWithoutAnImport()
    {
        Assert.Equal("3\n", Run.Program("use terminal library\ndefine int text 3\nwrite_line text"));
    }

    [Fact]
    public void AnUnknownStyleLiteralIsATypeError()
    {
        var error = ViewFailure("write_view in_style \"loud\" text \"x\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Contains("loud", error.Message);
        Assert.Equal(3, error.Position.Line);
        Assert.Equal(21, error.Position.Column);
    }

    [Fact]
    public void AnUnknownComputedStyleFailsWhenShown()
    {
        var error = ViewFailure("define string mood \"loud\"\nwrite_view in_style mood text \"x\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains("loud", error.Message);
    }

    [Fact]
    public void InColorIsDroppedLikeAStyleButTheInnermostWins()
    {
        Assert.Equal(
            "<warning>Hero\n</warning><bright_red>Dragon\n</bright_red><warning>ok\n</warning>",
            Views("styled \"warning\" write_view panel \"Hero\" rows list_of View in_color \"bright_red\" text \"Dragon\" text \"ok\" end"));
    }

    [Fact]
    public void AStyleInsideAColorKeepsItsStyle()
    {
        Assert.Equal("<danger>x\n</danger>", Views("write_view in_color \"green\" in_style \"danger\" text \"x\""));
    }

    [Fact]
    public void AColoredGridWritesItsLinesWithoutColor()
    {
        Assert.Equal(
            "#g#\n#.\n",
            Views("write_view colored_grid list_of string \"#g#\" \"#.\" end map_of string string \"g\" \"green\" \"#\" \"gray\" end 0 0"));
    }

    [Fact]
    public void AColoredGridsTextFormShowsItsMap()
    {
        Assert.Equal(
            "colored_grid [\"@\"] {\"@\": \"bright_white\"} 0 0\n",
            Views("write_line colored_grid list_of string \"@\" end map_of string string \"@\" \"bright_white\" end 0 0"));
    }

    [Theory]
    [InlineData("write_view in_color \"pink\" text \"x\"", "Unknown color \"pink\"")]
    [InlineData("write_view in_color \"Red\" text \"x\"", "Unknown color \"Red\"")]
    [InlineData("write_view colored_grid list_of string \"#\" end map_of string string \"#\" \"danger\" end 0 0", "Unknown color \"danger\"")]
    [InlineData("write_view colored_grid list_of string \"#\" end map_of string string \"##\" \"gray\" end 0 0", "must be one character that is not a control character, found \"##\"")]
    [InlineData("write_view colored_grid list_of string \"#\" end map_of string string \"\" \"gray\" end 0 0", "must be one character that is not a control character, found \"\"")]
    [InlineData("write_view colored_grid list_of string \"#\" end map_of string string \"\\t\" \"gray\" end 0 0", "found \"\\t\"")]
    public void ColorLiteralsAreChecked(string body, string message)
    {
        var error = ViewFailure(body);

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void AStyleNameIsNotAColor()
    {
        var error = ViewFailure("write_view in_color \"warning\" text \"x\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Contains("the colors are black, red, green", error.Message);
    }

    [Fact]
    public void AColorNameIsNotAStyle()
    {
        Assert.Equal(ErrorStage.Type, ViewFailure("write_view in_style \"red\" text \"x\"").Stage);
        Assert.Equal(ErrorStage.Type, Run.Failure("styled \"red\" write_line \"x\"").Stage);
    }

    [Theory]
    [InlineData("define string tint \"pink\"\nwrite_view in_color tint text \"x\"", "Unknown color \"pink\"")]
    [InlineData("define string tint \"pink\"\nwrite_view colored_grid list_of string \"#\" end map_of string string \"#\" tint end 0 0", "Unknown color \"pink\"")]
    [InlineData("define string key \"ab\"\nwrite_view colored_grid list_of string \"#\" end map_of string string key \"gray\" end 0 0", "found \"ab\"")]
    [InlineData("define string key \"\\n\"\nwrite_view colored_grid list_of string \"#\" end map_of string string key \"gray\" end 0 0", "found \"\\n\"")]
    public void ComputedColorsAreCheckedWhenShown(string body, string message)
    {
        var error = ViewFailure(body);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void AKeyOfOneCharacterIsCountedLikeLengthOf()
    {
        Assert.Equal("é\n", Views("write_view colored_grid list_of string \"é\" end map_of string string \"é\" \"red\" end 0 0"));
    }

    [Fact]
    public void AttemptGivesFalseForABadColor()
    {
        Assert.Equal("false\n", Views("define string tint \"pink\"\nwrite_line attempt write_view in_color tint text \"x\""));
    }

    [Fact]
    public void ATableWithoutHeadersFailsWhenShown()
    {
        Assert.Equal(
            "A table needs at least one header.",
            ViewFailure("show table list_of string end list_of list string end").Message);
    }

    [Fact]
    public void ATableRowOfTheWrongSizeFailsBeforeAnythingIsWritten()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        Assert.Throws<PyropException>(() => session.Run(Terminal +
            "write_view rows list_of View text \"before\" table list_of string \"a\" \"b\" end list_of list string list_of string \"1\" \"2\" end list_of string \"1\" \"2\" \"3\" end end end"));
        Assert.Equal("", output.ToString());
        Assert.Equal(
            "Row 1 of a table has 3 cells, but the table has 2 headers; rows are numbered from 0.",
            ViewFailure("write_view table list_of string \"a\" \"b\" end list_of list string list_of string \"1\" \"2\" end list_of string \"1\" \"2\" \"3\" end end").Message);
    }

    [Fact]
    public void AttemptGivesFalseForAViewThatCannotBeShown()
    {
        Assert.Equal("false\n", Views("write_line attempt show table list_of string end list_of list string end"));
        Assert.Equal("x\ntrue\n", Views("define bool shown attempt write_view text \"x\"\nwrite_line shown"));
    }

    [Fact]
    public void AViewNestedTooDeeplyIsNotShown()
    {
        const string build = "define View view text \"core\"\ndefine int depth 0\nwhile less depth {0} block\n    set view panel \"p\" view\n    set depth add depth 1\nend\n";

        var deep = ViewFailure(string.Format(build, 1000) + "write_view view");
        Assert.Equal(ErrorStage.Runtime, deep.Stage);
        Assert.Equal("This view is nested too deeply to be shown.", deep.Message);
        Assert.Equal("false\n", Views(string.Format(build, 1000) + "write_line attempt show view"));
        Assert.EndsWith("p\ncore\n", Views(string.Format(build, 999) + "write_view view"));
    }

    [Fact]
    public void ReadKeyReadsOneLinePerKey()
    {
        Assert.Equal("down\nenter\n", Views("write_line read_key\nwrite_line read_key", "down\nenter\n"));
    }

    [Fact]
    public void ReadKeyFailsAtTheEndOfInput()
    {
        var error = ViewFailure("write_line read_key");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("'read_key' reached the end of input.", error.Message);
        Assert.Equal("none\n", Views("write_line attempt read_key"));
    }

    [Fact]
    public void ReadKeyWithinGivesAKeyOrATimeout()
    {
        Assert.Equal("some \"q\"\nnone\nnone\n", Views(
            "write_line read_key_within 1000\nwrite_line read_key_within 1000\nwrite_line read_key_within 0", "q\n\n"));
    }

    [Fact]
    public void ReadKeyWithinWaitsAtTheEndOfInput()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal("none\n", Views("write_line read_key_within 200"));
        Assert.True(watch.ElapsedMilliseconds >= 150);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("2147483648")]
    public void ReadKeyWithinRejectsATimeOutOfRange(string time)
    {
        var error = ViewFailure($"write_line read_key_within {time}");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"'read_key_within' needs a time from 0 to 2147483647 milliseconds, found {time}.", error.Message);
    }

    [Fact]
    public void ADashboardLoopReadsKeysOnATimer()
    {
        Assert.Equal("Lines: 0 of 0\n\nLines: 1 of 1\n\nbye\n", Views(
            "define int count 0\n" +
            "while true block\n" +
            "    show bar \"Lines\" count count\n" +
            "    if_some string key read_key_within 1000 block\n" +
            "        if equal key \"q\" block\n" +
            "            write_line \"bye\"\n" +
            "            exit 0\n" +
            "        end\n" +
            "    end\n" +
            "    else set count add count 1\n" +
            "end",
            "\nq\n"));
    }
}
