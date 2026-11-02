namespace Pyrop.Tests;

public class AttemptTests
{
    private const string Direction = "choice Direction north east south west end\n";

    private static PyropError Failure(string source, ErrorStage stage, int line, int column, string input = "")
    {
        var error = Run.Failure(source, input);
        Assert.Equal(stage, error.Stage);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
        return error;
    }

    [Theory]
    [InlineData("add 9223372036854775807 1")]
    [InlineData("subtract -9223372036854775807 2")]
    [InlineData("multiply 9223372036854775807 2")]
    [InlineData("divide 1 0")]
    [InlineData("divide 1.0 0.0")]
    [InlineData("divide -9223372036854775807 0")]
    [InlineData("modulo 1 0")]
    [InlineData("to_power 10.0 400.0")]
    [InlineData("to_power -8.0 0.5")]
    [InlineData("to_power 2 -1")]
    [InlineData("to_power 10 40")]
    [InlineData("square_root -1.0")]
    [InlineData("round 100000000000000000000000.0")]
    [InlineData("round_down -100000000000000000000000.0")]
    [InlineData("abs subtract -9223372036854775807 1")]
    [InlineData("subtract 0 subtract -9223372036854775807 1")]
    [InlineData("random 1.0 1.0")]
    [InlineData("roll_dice 2 1")]
    [InlineData("read")]
    [InlineData("ask \"Name? \"")]
    [InlineData("confirm \"Sure?\"")]
    [InlineData("choose \"Where?\" list_of string \"Hall\" end")]
    [InlineData("choose \"Where?\" list_of string end")]
    [InlineData("argument_at 0")]
    [InlineData("read_environment \"\"")]
    [InlineData("read_environment \"A=B\"")]
    [InlineData("read_file \"no such file for attempt.txt\"")]
    [InlineData("read_file \"\"")]
    [InlineData("read_lines \"no such file for attempt.txt\"")]
    [InlineData("list_files \"no such directory for attempt\"")]
    [InlineData("substring \"abc\" 2 5")]
    [InlineData("get \"abc\" 3")]
    [InlineData("find_from \"abc\" \"a\" 9")]
    [InlineData("replace \"abc\" \"\" \"x\"")]
    [InlineData("repeat \"a\" -1")]
    [InlineData("repeat \"ab\" 60000000")]
    [InlineData("pad_left \"a\" 3 \"xy\"")]
    [InlineData("pad_right \"a\" 3 \"\"")]
    [InlineData("split \"a\" \"\"")]
    [InlineData("get list_of int 1 end 1")]
    [InlineData("put list_of int 1 end -1 2")]
    [InlineData("remove list_of int end 0")]
    [InlineData("get map_of string int end \"lamp\"")]
    [InlineData("remove put map_of int bool end 1 true 2")]
    [InlineData("convert int \"abc\"")]
    [InlineData("convert int \" \"")]
    [InlineData("convert float \"1e999\"")]
    [InlineData("convert bool \"yes\"")]
    [InlineData("convert int 100000000000000000000000.0")]
    [InlineData("convert Direction \"up\"")]
    [InlineData("convert Direction \"North\"")]
    public void EveryFailingOperationFailsTheSameWayWithAndWithoutAttempt(string operation)
    {
        var plain = Run.Failure($"{Direction}write_line {operation}");
        Assert.Equal(ErrorStage.Runtime, plain.Stage);
        Assert.Equal((2, 12), (plain.Position.Line, plain.Position.Column));

        Assert.EndsWith("none\n", Run.Program($"{Direction}write_line attempt {operation}"));
        Assert.EndsWith("caught\n", Run.Program($"{Direction}if equal attempt {operation} attempt {operation} write_line \"caught\""));
    }

    [Theory]
    [InlineData("write_file \"no such directory for attempt/save.txt\" \"data\"")]
    [InlineData("append_file \"no such directory for attempt/save.txt\" \"data\"")]
    [InlineData("write_file \"\" \"data\"")]
    [InlineData("write_file \".\" \"data\"")]
    [InlineData("write_table list_of string end list_of list string end")]
    [InlineData("write_table list_of string \"a\" end list_of list string list_of string \"b\" \"c\" end end")]
    public void FailingVoidOperationsGiveFalse(string operation)
    {
        var plain = Run.Failure(operation);
        Assert.Equal(ErrorStage.Runtime, plain.Stage);

        Assert.Equal("false\n", Run.Program($"write_line attempt {operation}"));
        Assert.Equal("Could not save.\n", Run.Program($"if not attempt {operation} write_line \"Could not save.\""));
    }

    [Fact]
    public void SucceedingVoidOperationsGiveTrueAndTakeEffect()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pyrop-attempt-{Guid.NewGuid():N}.txt").Replace("\\", "\\\\");
        try
        {
            var source = $"""
                write_line attempt write_file "{path}" "one"
                write_line attempt append_file "{path}" " two"
                write_line read_file "{path}"
                write_line attempt seed_random 7
                """;

            Assert.Equal("true\ntrue\none two\ntrue\n", Run.Program(source));
        }
        finally
        {
            File.Delete(path.Replace("\\\\", "\\"));
        }
    }

    [Fact]
    public void SucceedingOperationsGiveTheirResultInsideSome()
    {
        const string source = """
            choice Direction north east south west end
            write_line attempt add 1 2
            write_line attempt convert int " 42 "
            write_line attempt convert Direction " west "
            write_line attempt convert string 3.0
            write_line attempt get list_of string "lamp" "key" end 1
            write_line attempt get put map_of string int end "lamp" 5 "lamp"
            write_line attempt read
            write_line attempt split "a,b" ","
            write_line attempt not true
            write_line attempt ask "? "
            write_line attempt input_ended
            """;

        Assert.Equal(
            "some 3\nsome 42\nsome west\nsome \"3.0\"\nsome \"key\"\nsome 5\nsome \"typed\"\nsome [\"a\", \"b\"]\nsome false\n? some \"again\"\nsome true\n",
            Run.Program(source, "typed\nagain\n"));
    }

    [Fact]
    public void ResultTypesAreOptionsOfTheOperationsResultOrBool()
    {
        const string source = """
            def option int number attempt convert int "5"
            def option string text attempt read_file "no such file for attempt.txt"
            def option list string pieces attempt split "a" ","
            def option float half attempt divide 1.0 2.0
            def bool saved attempt write_file "" "x"
            def option bool flag attempt equal 1 1
            write_line number write_line text write_line pieces write_line half write_line saved write_line flag
            """;

        Assert.Equal("some 5\nnone\nsome [\"a\"]\nsome 0.5\nfalse\nsome true\n", Run.Program(source));
    }

    [Fact]
    public void ResultTypeIsCheckedLikeAnyOtherType()
    {
        var error = Failure("def int n attempt convert int \"5\"", ErrorStage.Type, 1, 11);
        Assert.Equal("Cannot initialize int variable 'n' with a value of type option int.", error.Message);

        error = Failure("def option int b attempt write_file \"a\" \"b\"", ErrorStage.Type, 1, 18);
        Assert.Equal("Cannot initialize option int variable 'b' with a value of type bool.", error.Message);

        error = Failure("write_line attempt add 1 2.0", ErrorStage.Type, 1, 20);
        Assert.StartsWith("'add' cannot take int float.", error.Message);

        error = Failure("write_line attempt convert bool 1", ErrorStage.Type, 1, 20);
        Assert.Equal("Cannot convert int to bool.", error.Message);
    }

    [Fact]
    public void AFailingArgumentStaysARuntimeError()
    {
        var error = Failure("write_line attempt divide 10 convert int \"x\"", ErrorStage.Runtime, 1, 30);
        Assert.Equal("Cannot convert \"x\" to int.", error.Message);

        error = Failure("write_line attempt convert int read_file \"no such file for attempt.txt\"", ErrorStage.Runtime, 1, 32);
        Assert.StartsWith("File \"no such file for attempt.txt\"", error.Message);

        error = Failure("write_line attempt get list_of int 1 end divide 1 0", ErrorStage.Runtime, 1, 42);
        Assert.Equal("Division by zero.", error.Message);
    }

    [Fact]
    public void AFailureInsideAUserFunctionCalledByAnArgumentIsNotCaught()
    {
        const string source = """
            function string path_of string name
                return add "dir/" get name 10
            write_line attempt read_file path_of "save"
            """;

        var error = Failure(source, ErrorStage.Runtime, 2, 23);
        Assert.StartsWith("Position 10 is out of range", error.Message);
    }

    [Fact]
    public void ExitAndTheCallDepthLimitInAnArgumentAreNotCaught()
    {
        const string exits = """
            function string stop
            block
                exit 3
                return ""
            end
            write_line attempt read_file stop
            write_line "not reached"
            """;

        Assert.Equal(("", 3), Run.Execute(exits));

        const string recursive = """
            function int deep int n return deep add n 1
            write_line attempt divide 1 deep 0
            """;

        var error = Failure(recursive, ErrorStage.Runtime, 1, 32);
        Assert.StartsWith("Call depth limit of 10000 exceeded", error.Message);
    }

    [Fact]
    public void AnAttemptInsideAnArgumentCatchesOnlyItsOwnFailure()
    {
        Assert.Equal("none\n", Run.Program("write_line attempt divide 10 value_or attempt convert int \"x\" 0"));
        Assert.Equal("some 5\n", Run.Program("write_line attempt divide 10 value_or attempt convert int \"x\" 2"));
        Assert.Equal("some some none\n", Run.Program("write_line attempt some attempt convert int \"x\""));

        var error = Failure("write_line value_or attempt get list_of int 1 end 3 divide 1 0", ErrorStage.Runtime, 1, 53);
        Assert.Equal("Division by zero.", error.Message);
    }

    [Fact]
    public void AttemptCatchesFailuresInEveryCallOfARecursiveFunction()
    {
        const string source = """
            function int parse_all list string texts int index
            block
                if equal index length_of texts return 0
                return add value_or attempt convert int get texts index 0 parse_all texts add index 1
            end
            write_line parse_all list_of string "1" "x" "30" "" "4" end 0
            """;

        Assert.Equal("35\n", Run.Program(source));
    }

    [Fact]
    public void ReadEnvironmentTellsUnsetFromEmptyOnlyUnderAttempt()
    {
        var host = new Runtime.HostContext([], name => name == "EMPTY" ? "" : name == "HOME" ? "/home/ann" : null);
        const string source = """
            write_line attempt read_environment "HOME"
            write_line attempt read_environment "EMPTY"
            write_line attempt read_environment "UNSET"
            write_line equal read_environment "UNSET" read_environment "EMPTY"
            write_line value_or attempt read_environment "UNSET" "fallback"
            """;

        Assert.Equal("some \"/home/ann\"\nsome \"\"\nnone\ntrue\nfallback\n", Run.Program(source, host: host));
    }

    [Fact]
    public void AttemptReadsInputUntilItEnds()
    {
        const string source = """
            def int total 0
            def bool reading true
            while reading block
                if_some string line attempt read set total add total value_or attempt convert int line 0
                else set reading false
            end
            write_line total
            """;

        Assert.Equal("10\n", Run.Program(source, "4\nfour\n6\n"));
    }

    [Fact]
    public void AttemptCatchesTheCollectionSizeLimit()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { CollectionLimit = 2 };
        const string source = """
            write_line attempt push list_of int 1 2 end 3
            write_line attempt put put put map_of int int end 1 1 2 2 3 3
            write_line attempt push list_of int 1 end 2
            """;

        session.Run(source);
        Assert.Equal("none\nnone\nsome [1, 2]\n", output.ToString());
    }

    private const string Deep =
        "def list list list list int deep list_of list list list int list_of list list int list_of list int list_of int 1 end end end end\n" +
        "def list list list list int copy list_of list list list int list_of list list int list_of list int list_of int 1 end end end end\n";

    [Theory]
    [InlineData("write_line attempt equal deep copy", 20, "'equal' cannot compare values nested this deeply.")]
    [InlineData("write_line attempt has list_of list list list list int deep end copy", 20, "'has' cannot compare values nested this deeply.")]
    [InlineData("write_line attempt convert string deep", 20, "This value is nested too deeply to be written as text.")]
    [InlineData("write_line convert string deep", 12, "This value is nested too deeply to be written as text.")]
    public void TheNestingLimitIsNotCaughtByAttempt(string line, int column, string message)
    {
        var session = new PyropSession(new StringReader(""), new StringOutput()) { NestingLimit = 3 };
        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(Deep + line)).Errors);
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal((3, column), (error.Position.Line, error.Position.Column));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void AFailingNestedOperationArgumentIsNotCaught()
    {
        var error = Failure("write_line attempt divide 10 divide 1 0", ErrorStage.Runtime, 1, 30);
        Assert.Equal("Division by zero.", error.Message);
    }

    [Fact]
    public void AttemptKeepsTheShortCircuitOfAndAndOr()
    {
        const string source = """
            function bool stop
            block
                exit 4
                return true
            end
            write_line attempt and false stop
            write_line attempt or true stop
            write_line attempt and true stop
            """;

        Assert.Equal(("some false\nsome true\n", 4), Run.Execute(source));
    }

    [Theory]
    [InlineData("write_line attempt 5", 1, 20, "found '5'.")]
    [InlineData("write_line attempt \"text\"", 1, 20, "found string \"text\".")]
    [InlineData("def int x 1 write_line attempt x", 1, 32, "found 'x'.")]
    [InlineData("write_line attempt attempt read", 1, 20, "found 'attempt'.")]
    [InlineData("write_line attempt map_of string int end", 1, 20, "found 'map_of'.")]
    [InlineData("write_line attempt none int", 1, 20, "found 'none'.")]
    [InlineData("write_line attempt list_of int end", 1, 20, "found 'list_of'.")]
    [InlineData("write_line attempt true", 1, 20, "found 'true'.")]
    [InlineData("record Point float x float y\nwrite_line attempt Point 1.0 2.0", 2, 20, "found 'Point'.")]
    [InlineData("choice Direction north end\nwrite_line attempt north", 2, 20, "found 'north'.")]
    public void TheOperandMustBeABuiltinOperationOrAConvert(string source, int line, int column, string ending)
    {
        var error = Failure(source, ErrorStage.Parse, line, column);
        Assert.StartsWith(
            "'attempt' takes a built-in operation or a convert, such as attempt convert int read or attempt read_file \"notes.txt\"; ",
            error.Message);
        Assert.EndsWith(ending, error.Message);
    }

    [Fact]
    public void AUserFunctionCannotBeAttempted()
    {
        const string source = """
            function string load string path return read_file path
            write_line attempt load "save.txt"
            """;

        var error = Failure(source, ErrorStage.Parse, 2, 20);
        Assert.Equal(
            "'attempt' takes a built-in operation or a convert, such as attempt convert int read or attempt read_file \"notes.txt\"; " +
            "found the function 'load'; a function's own failures are runtime errors, so that a bug in it is never mistaken for bad input.",
            error.Message);
    }

    [Fact]
    public void AQualifiedNameCannotBeAttempted()
    {
        var directory = Directory.CreateTempSubdirectory("pyrop-attempt-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "lib.prp"), "export function string load return \"x\"\n");
            var main = Path.Combine(directory.FullName, "main.prp");
            const string source = "use lib \"lib.prp\"\nwrite_line attempt from lib load";
            var exception = Assert.Throws<PyropException>(
                () => new PyropSession(new StringReader(""), new StringOutput()).Run(source, "main.prp", main));
            var error = Assert.Single(exception.Errors);
            Assert.Equal(ErrorStage.Parse, error.Stage);
            Assert.EndsWith("found the qualified name starting at 'from'; only built-in operations can follow 'attempt'.", error.Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AttemptIsNotAStatement()
    {
        var error = Failure("attempt write_file \"save.txt\" \"data\"", ErrorStage.Parse, 1, 1);
        Assert.Equal(
            "'attempt' cannot be a statement: its result tells whether the operation succeeded. Use it as a value, as in if not attempt write_file \"save.txt\" text ….",
            error.Message);
    }

    [Fact]
    public void AVoidOperationIsStillNotAValueWithoutAttempt()
    {
        var error = Failure("def bool saved write_file \"a\" \"b\"", ErrorStage.Parse, 1, 16);
        Assert.Equal("Operation 'write_file' returns void and cannot be used as a value.", error.Message);
    }

    [Theory]
    [InlineData("write_line attempt")]
    [InlineData("write_line attempt convert int")]
    [InlineData("write_line attempt read_file")]
    public void AnUnfinishedAttemptIsIncompleteInput(string source)
    {
        var exception = Assert.Throws<PyropException>(() => Run.Program(source));
        Assert.True(exception.IsIncompleteInput);
        Assert.Equal(InputStatus.Incomplete, new PyropSession(new StringReader(""), new StringOutput()).Probe(source));
    }

    [Fact]
    public void AttemptIsAReservedWord()
    {
        var error = Failure("def int attempt 1", ErrorStage.Parse, 1, 9);
        Assert.Equal("'attempt' is a reserved word and cannot be used as a variable name.", error.Message);
    }

    [Fact]
    public void AnInputWithAnAttemptStaysAtomicInTheRepl()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);
        session.Run("def option int n attempt convert int \"7\"");
        Assert.Throws<PyropException>(() => session.Run("def option int m attempt convert int \"8\" set n m write_line attempt foo"));
        session.Run("write_line n write_line attempt convert int \"x\"");
        Assert.Equal("some 7\nnone\n", output.ToString());
    }
}
