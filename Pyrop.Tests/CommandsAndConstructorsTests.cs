using Pyrop.Cli;
using Pyrop.LanguageServer;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public sealed class CommandsAndConstructorsTests
{
    private sealed class FakeCommands(CommandOutcome outcome) : ICommandRunner
    {
        public List<(string Command, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public CommandOutcome Capture(string command, IReadOnlyList<string> arguments)
        {
            Calls.Add((command, arguments));
            return outcome;
        }

        public int RunAttached(string command, IReadOnlyList<string> arguments)
        {
            Calls.Add((command, arguments));
            return outcome.ExitCode;
        }
    }

    private static HostContext WithCommands(ICommandRunner commands) => HostContext.Isolated with { Commands = commands };

    private static HostContext RealCommands => WithCommands(ProcessCommandRunner.Instance);

    private const string Uses = "use program library\nimport from program CommandResult run run_attached end\n";

    [Fact]
    public void RunGivesTheExitCodeOutputAndErrors()
    {
        var commands = new FakeCommands(new CommandOutcome(3, "out", "err"));

        var output = Run.Program(Uses + "def CommandResult r run \"tool\" list_of string \"a b\" \"c\" end\nwrite_line r\n", host: WithCommands(commands));

        Assert.Equal("CommandResult exit_code 3 output \"out\" errors \"err\"\n", output);
        var call = Assert.Single(commands.Calls);
        Assert.Equal("tool", call.Command);
        Assert.Equal(["a b", "c"], call.Arguments);
    }

    [Fact]
    public void RunAttachedGivesTheExitCode()
    {
        var output = Run.Program(Uses + "write_line run_attached \"tool\" list_of string end\n", host: WithCommands(new FakeCommands(new CommandOutcome(7, "", ""))));

        Assert.Equal("7\n", output);
    }

    [Fact]
    public void ANonZeroExitIsANormalResult()
    {
        var output = Run.Program(Uses + "if_some CommandResult r attempt run \"tool\" list_of string end write_line the exit_code of r\n", host: WithCommands(new FakeCommands(new CommandOutcome(1, "", ""))));

        Assert.Equal("1\n", output);
    }

    [Fact]
    public void AHostWithoutCommandsFails()
    {
        Assert.Contains("this host does not run commands", Run.Failure(Uses + "def CommandResult r run \"git\" list_of string end\n").Message);
    }

    [Fact]
    public void AttemptTurnsAHostWithoutCommandsIntoNone()
    {
        Assert.Equal("true\n", Run.Program(Uses + "write_line is_none attempt run \"git\" list_of string end\n"));
    }

    [Theory]
    [InlineData("\"\"", "needs a program name")]
    public void ABadCommandNameIsARuntimeError(string name, string message)
    {
        Assert.Contains(message, Run.Failure(Uses + $"def CommandResult r run {name} list_of string end\n", host: RealCommands).Message);
    }

    [Theory]
    [InlineData("\\\"cmd\\\" /c \\\"echo x\\\"")]
    [InlineData("  cmd  ")]
    [InlineData("   ")]
    public void AProgramNameCannotCarryArguments(string name)
    {
        var host = RealCommands;

        Assert.Contains("A program name cannot", Run.Failure(Uses + $"def CommandResult r run \"{name}\" list_of string end\n", host: host).Message);
    }

    [Theory]
    [InlineData("tool.bat")]
    [InlineData("C:\\tools\\tool.CMD")]
    [InlineData("tool.bat.")]
    public void BatchFilesAreRefusedOnWindows(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("through cmd", Assert.Throws<CommandFailedException>(() => ProcessCommandRunner.Instance.Capture(name, [])).Message);
        }
    }

    [Fact]
    public void AFolderAsTheCommandGivesAPlainMessage()
    {
        var reason = OperatingSystem.IsWindows() ? "it is not a program that can be started, or access to it was denied." : "no such program was found.";

        Assert.Equal($"Cannot run \".\": {reason}", Assert.Throws<CommandFailedException>(() => ProcessCommandRunner.Instance.Capture(".", [])).Message);
    }

    [Fact]
    public void OutputPastTheLimitStopsTheProgram()
    {
        var failure = Assert.Throws<CommandFailedException>(() => new ProcessCommandRunner(5).Capture("git", ["--version"]));

        Assert.Contains("more than 5 characters", failure.Message);
    }

    [Fact]
    public void HeavyOutputOnBothStreamsDoesNotBlock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var outcome = ProcessCommandRunner.Instance.Capture("cmd", ["/c", "for /L %i in (1,1,3000) do @(echo a line of output to fill the pipe& echo a line of errors to fill the pipe 1>&2)"]);

        Assert.Equal(3000, outcome.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(3000, outcome.Errors.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void TheProcessHostRunsAttachedCommands()
    {
        var output = new StringOutput();

        new PyropSession(TextReader.Null, output, HostContext.ForProcess([])).Run(Uses + "write_line run_attached \"git\" list_of string \"--version\" end\n");

        Assert.Equal("0\n", output.ToString());
    }

    private static int TimesShown(string between)
    {
        var source = "use program library\nuse terminal library\nimport from terminal View panel text show end\n"
            + "show panel \"A\" text \"same\"\n" + between + "show panel \"A\" text \"same\"\n";
        return Run.Program(source, host: WithCommands(new FakeCommands(new CommandOutcome(0, "", "")))).Split("same").Length - 1;
    }

    [Fact]
    public void APlainScreenIsWrittenInFullAfterAnAttachedCommand()
    {
        Assert.Equal(1, TimesShown(""));
        Assert.Equal(2, TimesShown("def int code from program run_attached \"tool\" list_of string end\n"));
    }

    [Fact]
    public void AQualifiedTypeAndCallWork()
    {
        const string source = "use program library\ndef from program CommandResult r from program run \"tool\" list_of string end\nwrite_line the output of r\n";

        Assert.Equal("hi\n", Run.Program(source, host: WithCommands(new FakeCommands(new CommandOutcome(0, "hi", "")))));
    }

    [Fact]
    public void ARealProgramRuns()
    {
        var output = Run.Program(Uses + "def CommandResult r run \"git\" list_of string \"--version\" end\nwrite_line starts_with the output of r \"git version\"\nwrite_line the exit_code of r\n", host: RealCommands);

        Assert.Equal("true\n0\n", output);
    }

    [Fact]
    public void ARealFailureFillsErrorsAndExitCode()
    {
        var output = Run.Program(Uses + "def CommandResult r run \"git\" list_of string \"no-such-subcommand\" end\nwrite_line greater the exit_code of r 0\nwrite_line greater length_of the errors of r 0\n", host: RealCommands);

        Assert.Equal("true\ntrue\n", output);
    }

    [Fact]
    public void AProgramThatCannotBeFoundFails()
    {
        Assert.Contains("no such program was found", Run.Failure(Uses + "def CommandResult r run \"pyrop_no_such_program\" list_of string end\n", host: RealCommands).Message);
    }

    [Fact]
    public void WindowsLineEndsArePlain()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var outcome = ProcessCommandRunner.Instance.Capture("cmd", ["/c", "echo one& echo two"]);

        Assert.Equal("one\ntwo\n", outcome.Output.Replace(" \n", "\n"));
    }

    [Fact]
    public void AnArgumentWithSpacesAndQuotesArrivesWhole()
    {
        var outcome = ProcessCommandRunner.Instance.Capture("git", ["-c", "alias.say=!echo", "say", "a \"b\" c"]);

        Assert.Equal(0, outcome.ExitCode);
        Assert.Contains("a b c", outcome.Output.Replace("\"", ""));
    }

    [Fact]
    public void ACommandThatReadsInputSeesItsEnd()
    {
        var outcome = ProcessCommandRunner.Instance.Capture("git", ["hash-object", "--stdin"]);

        Assert.Equal("e69de29bb2d1d6434b8b29ae775ad8c2e48c5391\n", outcome.Output);
    }

    [Theory]
    [InlineData("for Direction way in variants_of Direction write_line way\n", "north\neast\nsouth\n")]
    [InlineData("write_line length_of variants_of Direction\n", "3\n")]
    [InlineData("write_line get variants_of Direction 1\n", "east\n")]
    public void VariantsOfListsInDeclarationOrder(string use, string output)
    {
        Assert.Equal(output, Run.Program("choice Direction north east south end\n" + use));
    }

    [Fact]
    public void VariantsOfRejectsAChoiceWithData()
    {
        var error = Run.Failure("choice Action go int stay end\nwrite_line variants_of Action\n");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Contains("'go'", error.Message);
    }

    [Theory]
    [InlineData("write_line variants_of int\n")]
    [InlineData("record P int x\nwrite_line variants_of P\n")]
    public void VariantsOfRejectsAnythingButAChoice(string source)
    {
        Assert.Contains("is not one", Run.Failure(source).Message);
    }

    [Fact]
    public void VariantsOfRejectsTheLibraryView()
    {
        Assert.Contains("carries data", Run.Failure("use terminal library\nimport from terminal View end\nwrite_line variants_of View\n").Message);
    }

    [Fact]
    public void VariantsOfIsReserved()
    {
        Assert.Contains("reserved", Run.Failure("def int variants_of 1\n").Message);
    }

    private const string Point = "record Point int x int y\n";

    [Theory]
    [InlineData("Point with x 1 with y 2")]
    [InlineData("Point with y 2 with x 1")]
    [InlineData("Point 1 2")]
    public void LabelsAndPositionsMakeTheSameRecord(string construct)
    {
        Assert.Equal("Point x 1 y 2\n", Run.Program(Point + $"write_line {construct}\n"));
    }

    [Fact]
    public void LabelledValuesRunInWrittenOrder()
    {
        var output = Run.Program(Point + "write_line Point with y convert int read with x convert int read\n", "1\n2\n");

        Assert.EndsWith("Point x 2 y 1\n", output);
    }

    [Theory]
    [InlineData("Point with x 1 with z 2", "'Point' has no field 'z'; its fields are x, y.")]
    [InlineData("Point with x 1 with x 2", "Field 'x' of 'Point' is given twice.")]
    public void BadLabelsAreParseErrors(string construct, string message)
    {
        var error = Run.Failure(Point + $"write_line {construct}\n");

        Assert.Equal((ErrorStage.Parse, message), (error.Stage, error.Message));
    }

    [Fact]
    public void AMissingLabelNamesTheField()
    {
        Assert.StartsWith("'Point' still needs y:", Run.Failure(Point + "write_line Point with x 1\n").Message);
    }

    [Fact]
    public void AWrongTypeNamesTheLabelledField()
    {
        Assert.Contains("Field 'y' of 'Point' must be int", Run.Failure(Point + "write_line Point with y \"a\" with x 1\n").Message);
    }

    [Fact]
    public void ACopyAsANonFinalLabelledValueSaysWhatHappened()
    {
        var message = Run.Failure("record A int n\nrecord B A a int c\ndef A one A 1\nwrite_line B with a copy one with n 2 with c 3\n").Message;

        Assert.Contains("took the 'with'", message);
    }

    [Fact]
    public void AForgottenFieldAfterACopyDoesNotBlameTheCopy()
    {
        var message = Run.Failure("record A int n\nrecord B A a int c\ndef A one A 1\nwrite_line B with a copy one with n 2\n").Message;

        Assert.StartsWith("'B' still needs c:", message);
        Assert.DoesNotContain("took the", message);
    }

    [Fact]
    public void ACopyAsTheLastLabelledValueWorks()
    {
        Assert.Equal("B c 3 a (A n 2)\n", Run.Program("record A int n\nrecord B int c A a\ndef A one A 1\nwrite_line B with c 3 with a copy one with n 2\n"));
    }

    [Fact]
    public void ACopyOfALabelledConstructorTakesTheWithsAfterItsFields()
    {
        Assert.Equal("Point x 9 y 2\n", Run.Program(Point + "write_line copy Point with x 1 with y 2 with x 9\n"));
    }

    [Fact]
    public void AnImportedRecordCanBeLabelled()
    {
        var directory = Directory.CreateTempSubdirectory("pyrop-labels-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "shapes.prp"), "export record Point int x int y\n");
            const string source = "use shapes \"shapes.prp\"\nwrite_line from shapes Point with y 2 with x 1\n";
            var path = Path.Combine(directory, "main.prp");
            var output = new StringOutput();

            new PyropSession(TextReader.Null, output).Run(source, path, path);

            Assert.Equal("Point x 1 y 2\n", output.ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PyropSession Quiet() => new(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true));

    [Fact]
    public void TheTreeShowsLabelsAndVariantsOf()
    {
        var tree = Quiet().Inspect("choice D a b end\nrecord P int x int y\nwrite_line P with y add 1 2 with x 3\nwrite_line variants_of D\n", "main.prp").Text;

        Assert.Contains("P with y (add 1 2) with x 3", tree);
        Assert.Contains("variants_of D", tree);
    }

    [Fact]
    public void TheFormatterIndentsLabelsUnderTheirConstructor()
    {
        const string source = "record P int x int y\ndef P p P\nwith x 1\nwith y 2\nwrite_line p\n";

        Assert.Equal("record P int x int y\ndef P p P\n    with x 1\n    with y 2\nwrite_line p\n", Quiet().Format(source, "main.prp"));
    }

    [Fact]
    public void HoverAndDefinitionUnderstandLabels()
    {
        const string source = "record A int x\nrecord B int x\nwrite_line B with x 1\n";
        var analyzed = Quiet().Analyze(source, "main.prp");

        Assert.NotNull(analyzed);
        Assert.Contains("A field of `B`", HoverProvider.Describe(analyzed, 3, 19)!.Markdown);
        var target = DefinitionProvider.Find(analyzed, 3, 19);
        Assert.Equal((1, 13), (target!.Line, target.Start));
    }

    [Fact]
    public void LibraryTypesAndVariantsHaveNoDefinition()
    {
        const string source = "use terminal library\nimport from terminal View text end\ndef View v text \"a\"\n";
        var analyzed = Quiet().Analyze(source, "main.prp");

        Assert.NotNull(analyzed);
        Assert.Null(DefinitionProvider.Find(analyzed, 3, 5));
        Assert.Null(DefinitionProvider.Find(analyzed, 3, 12));
    }

    [Fact]
    public void FieldsOfALibraryRecordAreCompleted()
    {
        const string source = "use program library\nwrite_line the exi";
        var analyzed = Quiet().Analyze(source, "main.prp");

        Assert.NotNull(analyzed);
        Assert.Equal("exit_code", Assert.Single(CompletionProvider.Complete(analyzed, 2, 18, typedSpace: false)).Label);
    }
}
