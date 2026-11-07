using Pyrop.Cli;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Tests;

public class StandardErrorTests
{
    [Fact]
    public void WriteErrorGoesToStandardErrorOnly()
    {
        Assert.Equal(("out\n", "usage: tool <file>\nwarn"), Run.Streams("write_line \"out\"\nwrite_error_line \"usage: tool <file>\"\nwrite_error \"warn\""));
    }

    [Fact]
    public void WriteErrorUsesTheTextForm()
    {
        Assert.Equal(("", "[1, 2]\n3.0true"), Run.Streams("write_error_line list_of int 1 2 end\nwrite_error 3.0\nwrite_error true"));
    }

    [Fact]
    public void StyledAppliesToWriteError()
    {
        Assert.Equal(
            ("<info>a</info>", "<danger>b\n</danger>"),
            Run.Streams("styled \"danger\" write_error_line \"b\"\nstyled \"info\" write \"a\""));
    }

    [Fact]
    public void ErrorTextBeforeARuntimeErrorIsKept()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        Assert.Throws<PyropException>(() => session.Run("write_error_line \"about to fail\"\nwrite_line divide 1 0"));

        Assert.Equal("about to fail\n", output.Errors);
    }

    [Theory]
    [InlineData("write_error_line 1 2", "cannot stand alone")]
    [InlineData("if true def int x 1", "cannot be the body")]
    public void WriteErrorFollowsTheStatementRules(string source, string message)
    {
        var error = Run.Failure(source);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void WriteErrorRejectsAVoidCall()
    {
        var error = Run.Failure("function void f return void\nwrite_error f");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(Run.Failure("function void f return void\nwrite f").Message, error.Message);
    }

    [Fact]
    public void WriteErrorWithoutAValueAsksForMoreInput()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());

        Assert.Equal(InputStatus.Incomplete, session.Probe("write_error_line"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("write_error add 1"));
        Assert.Equal(InputStatus.AwaitingElse, session.Probe("if true write_error \"x\""));
    }

    [Fact]
    public void TheTreeShowsWriteError()
    {
        var inspected = new PyropSession(new StringReader(""), new StringOutput())
            .Inspect("write_error add \"a\" \"b\"\nif true write_error_line split \"a b\" \" \"", "main.prp");

        Assert.Null(inspected.Error);
        Assert.Equal(
            "write_error (add \"a\" \"b\")\nif true\n    write_error_line (split \"a b\" \" \")\n",
            TreePrinter.Print(inspected.Tree!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APipedAnswerEndsTheLineOfTheStreamThatLeftItOpen(bool toErrors)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true));
        var session = new PyropSession(new StringReader("Ann\n"), console);

        session.Run($"{(toErrors ? "write_error" : "write")} \"Name: \"\ndef string name read\nwrite_line name");

        Assert.Equal(toErrors ? "Ann\n" : "Name: \nAnn\n", output.ToString());
        Assert.Equal(toErrors ? "Name: \n" : "", errors.ToString());
    }

    [Fact]
    public void ATypedAnswerAddsNoLineBreak()
    {
        var output = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, inputEchoed: true);

        new PyropSession(new StringReader("Ann\n"), console).Run("write \"Name: \"\nwrite_line read");

        Assert.Equal("Name: Ann\n", output.ToString());
    }

    [Fact]
    public void TheStreamsKeepTheirOwnLineStarts()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true));

        console.Write("a", OutputStyle.None);
        console.WriteError("b\n", OutputStyle.None);

        Assert.False(console.AtLineStart);
        Assert.True(console.ErrorsAtLineStart);
    }

    [Fact]
    public void AMessageStartsANewLineOfStandardErrorWithoutTouchingOutput()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true));

        console.Write("partial", OutputStyle.None);
        console.WriteError("warn", OutputStyle.None);
        console.BeginErrorMessage();

        Assert.Equal("partial", output.ToString());
        Assert.Equal("warn\n", errors.ToString());
        Assert.True(console.ErrorsAtLineStart);
    }

    [Fact]
    public void AMessageAfterFinishedErrorTextAddsNothing()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true));

        console.WriteError("warn\n", OutputStyle.None);
        console.BeginErrorMessage();

        Assert.Equal("warn\n", errors.ToString());
    }

    [Fact]
    public void OnASharedScreenTheLastWriteDecidesTheLineStart()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true), sharedScreen: true);

        console.Write("a", OutputStyle.None);
        console.WriteError("b\n", OutputStyle.None);
        console.EnsureLineStart();
        console.WriteError("c", OutputStyle.None);
        console.EnsureLineStart();

        Assert.Equal("a\n", output.ToString());
        Assert.Equal("b\nc", errors.ToString());
    }

    [Fact]
    public void OnASharedScreenAMessageStartsOnANewLineOnce()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true), sharedScreen: true);

        console.Write("done\n", OutputStyle.None);
        console.WriteError("warn", OutputStyle.None);
        console.BeginErrorMessage();

        Assert.Equal("done\n", output.ToString());
        Assert.Equal("warn\n", errors.ToString());
        Assert.True(console.ErrorsAtLineStart);
        Assert.True(console.AtLineStart);
    }

    [Fact]
    public void OnASharedScreenUnfinishedOutputGetsTheNewLine()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true), sharedScreen: true);

        console.WriteError("warn\n", OutputStyle.None);
        console.Write("partial", OutputStyle.None);
        console.BeginErrorMessage();

        Assert.Equal("partial\n", output.ToString());
        Assert.Equal("warn\n", errors.ToString());
    }

    [Fact]
    public void OnASharedScreenAFinishedLineGetsNothing()
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(output, plain: true, errors: new ConsoleStream(errors, plain: true), sharedScreen: true);

        console.WriteError("warn", OutputStyle.None);
        console.Write("done\n", OutputStyle.None);
        console.BeginErrorMessage();

        Assert.Equal("done\n", output.ToString());
        Assert.Equal("warn", errors.ToString());
    }

    [Fact]
    public void ATypedAnswerEndsTheLineOnAnErrorStreamOnScreen()
    {
        var errors = new StringWriter();
        var console = new ConsoleProgramOutput(
            new StringWriter(), plain: true, inputEchoed: true, errors: new ConsoleStream(errors, plain: true, onScreen: true));

        console.WriteError("Q? ", OutputStyle.None);
        console.LineRead();
        console.BeginErrorMessage();

        Assert.Equal("Q? ", errors.ToString());
    }

    [Fact]
    public void StyledErrorTextUsesAnsiSequencesUnlessPlain()
    {
        var styled = new StringWriter();
        var plain = new StringWriter();
        new ConsoleProgramOutput(TextWriter.Null, plain: true, errors: new ConsoleStream(styled, plain: false, newLine: "\r\n"))
            .WriteError("x\n", OutputStyle.Danger);
        new ConsoleProgramOutput(TextWriter.Null, plain: false, errors: new ConsoleStream(plain, plain: true))
            .WriteError("x\n", OutputStyle.Danger);

        Assert.Equal("\e[31mx\e[0m\r\n", styled.ToString());
        Assert.Equal("x\n", plain.ToString());
    }
}
