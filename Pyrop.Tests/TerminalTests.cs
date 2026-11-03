using Pyrop.Cli;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public class TerminalTests
{
    [Fact]
    public void StyledAppliesAStyleToItsStatement()
    {
        Assert.Equal("<warning>Low disk</warning>\n", Run.Program("styled \"warning\" write \"Low disk\"\nwrite \"\\n\""));
    }

    [Fact]
    public void TheInnermostStyleWinsAndTheOuterOneResumes()
    {
        const string source =
            "styled \"warning\" block\n    write \"a\"\n    styled \"emphasis\" write \"b\"\n    write \"c\"\nend\nwrite \"d\"";

        Assert.Equal("<warning>a</warning><emphasis>b</emphasis><warning>c</warning>d", Run.Program(source));
    }

    [Fact]
    public void AcceptsAComputedStyleName()
    {
        Assert.Equal("<success>ok</success>", Run.Program("def string mood \"success\"\nstyled mood write \"ok\""));
    }

    [Fact]
    public void RejectsAnUnknownStyleLiteralBeforeRunning()
    {
        var error = Run.Failure("write \"before\"\nstyled \"loud\" write \"x\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("Unknown style \"loud\"; the styles are emphasis, muted, success, warning, danger, info.", error.Message);
    }

    [Fact]
    public void RejectsAnUnknownComputedStyleWhileRunning()
    {
        var error = Run.Failure("def string mood \"Loud\"\nstyled mood write \"x\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("Unknown style \"Loud\"; the styles are emphasis, muted, success, warning, danger, info.", error.Message);
    }

    [Fact]
    public void RejectsAStyleThatIsNotAString()
    {
        var error = Run.Failure("styled 1 write \"x\"");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("The style of 'styled' must be string, found int.", error.Message);
    }

    [Fact]
    public void RejectsADefAsTheBody()
    {
        var error = Run.Failure("styled \"info\" def int x 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("A def cannot be the body of a styled; wrap it in block … end.", error.Message);
    }

    [Fact]
    public void ReservesWithStyle()
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure("def string styled \"x\"").Stage);
    }

    [Fact]
    public void ControlFlowPassesThroughWithStyle()
    {
        const string source =
            "function int sign int x styled \"muted\" if greater x 0 return 1 else return -1\n" +
            "while true styled \"info\" break\n" +
            "write_line sign 5";

        Assert.Equal("1\n", Run.Program(source));
    }

    [Fact]
    public void TheStyleEndsWhenControlLeavesItsStatement()
    {
        const string source =
            "function int one styled \"warning\" return 1\n" +
            "while true styled \"info\" break\n" +
            "write_line one";

        Assert.Equal("1\n", Run.Program(source));
    }

    [Theory]
    [InlineData(true, "Q: ")]
    [InlineData(false, "Q: \n")]
    public void ATypedAnswerCountsAsALineBreak(bool inputEchoed, string expected)
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: true, inputEchoed);

        output.WritePrompt("Q: ", null, OutputStyle.None);
        output.LineRead();
        output.EnsureLineStart();

        Assert.Equal(expected, writer.ToString());
    }

    [Fact]
    public void TheStyleEndsWhenItsStatementFails()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        Assert.Throws<PyropException>(() => session.Run("styled \"danger\" write_line divide 1 0"));
        session.Run("write \"plain\"");

        Assert.Equal("plain", output.ToString());
    }

    [Fact]
    public void AnOpenIfInsideWithStyleWaitsForElseInTheRepl()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());

        Assert.Equal(InputStatus.AwaitingElse, session.Probe("styled \"info\" if true write \"x\""));
    }

    [Fact]
    public void AskShowsThePromptAndReturnsTheAnswer()
    {
        Assert.Equal("Name: Hi Ann\n", Run.Program("write_line add \"Hi \" ask \"Name: \"", "Ann\n"));
    }

    [Fact]
    public void AskUsesTheCurrentStyleForItsPrompt()
    {
        Assert.Equal(
            "<info>Name: </info>Ann\n",
            Run.Program("def string name \"\"\nstyled \"info\" set name ask \"Name: \"\nwrite_line name", "Ann\n"));
    }

    [Fact]
    public void AskSharesTheInputBufferWithInputEnded()
    {
        Assert.Equal("Q: a\n", Run.Program("if not input_ended write_line ask \"Q: \"", "a\n"));
    }

    [Theory]
    [InlineData("y", "true")]
    [InlineData(" YES ", "true")]
    [InlineData("n", "false")]
    [InlineData("No", "false")]
    public void ConfirmAcceptsYesAndNo(string answer, string expected)
    {
        Assert.Equal($"Go? [y/n] {expected}\n", Run.Program("write_line confirm \"Go? \"", answer + "\n"));
    }

    [Fact]
    public void ConfirmAsksAgainUntilItGetsAnAnswer()
    {
        const string retry = "Go? [y/n] <warning>Please answer y or n.\n</warning>";

        Assert.Equal(retry + retry + "Go? [y/n] true\n", Run.Program("write_line confirm \"Go?\"", "maybe\n\ny\n"));
    }

    [Theory]
    [InlineData("write_line ask \"Q: \"", "", "'ask' reached the end of input.")]
    [InlineData("write_line confirm \"Go?\"", "maybe\n", "'confirm' reached the end of input.")]
    public void PromptsFailAtTheEndOfInput(string source, string input, string message)
    {
        var error = Run.Failure(source, input);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ConsoleOutputWritesStylesAsAnsiSequencesLineByLine()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false);

        output.Write("a\n\nb", OutputStyle.Warning);
        output.Write("c", OutputStyle.None);

        Assert.Equal("\e[33ma\e[0m\n\n\e[33mb\e[0mc", writer.ToString());
    }

    [Fact]
    public void ConsoleOutputCanEndLinesWithACarriageReturn()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false, newLine: "\r\n");

        output.Write("Hello, World!\n", OutputStyle.None);
        output.Write("a\nb\n", OutputStyle.Warning);

        Assert.Equal("Hello, World!\r\n\e[33ma\e[0m\r\n\e[33mb\e[0m\r\n", writer.ToString());
        Assert.True(output.AtLineStart);
    }

    [Fact]
    public void ConsoleOutputDecoratesPrompts()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: false);

        output.WritePrompt("Go?", " [y/n] ", OutputStyle.None);

        Assert.Equal("\e[36m? \e[0mGo?\e[90m [y/n] \e[0m", writer.ToString());
    }

    [Fact]
    public void PlainConsoleOutputWritesNoEscapeSequences()
    {
        var writer = new StringWriter();
        var output = new ConsoleProgramOutput(writer, plain: true);

        output.WritePrompt("Go?", " [y/n] ", OutputStyle.Emphasis);
        output.Write("done\n", OutputStyle.Danger);

        Assert.Equal("Go? [y/n] done\n", writer.ToString());
    }

    [Theory]
    [InlineData("with_style \"warning\" write_line 1")]
    [InlineData("write_line with_style")]
    public void WithStyleIsRetiredInFavourOfStyled(string source)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("'with_style' is no longer part of Pyrop; it is now called styled, as in styled \"warning\" write_line \"Careful\".", error.Message);
    }

    [Fact]
    public void StyledIsAReservedWord()
    {
        Assert.Contains("reserved word", Run.Failure("def int styled 1").Message);
    }
}
