using System.Diagnostics;
using Pyrop.Syntax;

namespace Pyrop.Tests;

public class ForTests
{
    private static PyropError Failure(string source, ErrorStage stage)
    {
        var error = Run.Failure(source);
        Assert.Equal(stage, error.Stage);
        return error;
    }

    private static string Tree(string source)
    {
        var inspected = new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, "main.prp");
        Assert.Null(inspected.Error);
        return TreePrinter.Print(inspected.Tree!);
    }

    [Fact]
    public void WalksEveryItemInOrder()
    {
        Assert.Equal(
            "lamp\nrope\nkey\n",
            Run.Program("for string item in list_of string \"lamp\" \"rope\" \"key\" end write_line item"));
    }

    [Fact]
    public void RunsABlockBodyForEachItem()
    {
        const string source = """
            def int total 0
            for int n in list_of int 1 2 3 4 end block
                set total add total n
                write_line total
            end
            write_line total
            """;

        Assert.Equal("1\n3\n6\n10\n10\n", Run.Program(source));
    }

    [Fact]
    public void AnEmptyListRunsTheBodyZeroTimes()
    {
        Assert.Equal("done\n", Run.Program("for int n in empty list int write_line n\nwrite_line \"done\""));
    }

    [Fact]
    public void WalksAMapThroughKeysOfInInsertionOrder()
    {
        const string source = """
            def map string int prices put put put empty map string int "rope" 2 "lamp" 5 "rope" 3
            for string name in keys_of prices block
                write add name ": "
                write_line value_of prices name
            end
            """;

        Assert.Equal("rope: 3\nlamp: 5\n", Run.Program(source));
    }

    [Fact]
    public void WalksNestedLists()
    {
        const string source = """
            def list list int grid list_of list int list_of int 1 2 end list_of int end list_of int 3 end end
            for list int row in grid block
                write_line length_of row
                for int cell in row write_line cell
            end
            """;

        Assert.Equal("2\n1\n2\n0\n1\n3\n", Run.Program(source));
    }

    [Fact]
    public void BreakAndContinueAffectOnlyTheInnermostFor()
    {
        const string source = """
            for int a in list_of int 1 2 3 4 end block
                if equal a 2 continue
                if equal a 4 break
                for int b in list_of int 10 20 30 40 end block
                    if equal b 20 continue
                    if equal b 40 break
                    write_line add a b
                end
            end
            """;

        Assert.Equal("11\n31\n13\n33\n", Run.Program(source));
    }

    [Fact]
    public void BreakInsideForInsideWhileLeavesOnlyTheFor()
    {
        const string source = """
            def int i 0
            while less i 2 block
                for int n in list_of int 1 2 3 end block
                    if equal n 2 break
                    write_line add i n
                end
                set i add i 1
            end
            """;

        Assert.Equal("1\n2\n", Run.Program(source));
    }

    [Fact]
    public void BreakInsideWhileInsideForLeavesOnlyTheWhile()
    {
        const string source = """
            for int n in list_of int 1 2 end block
                while true block
                    write_line n
                    break
                end
                write_line "after"
            end
            """;

        Assert.Equal("1\nafter\n2\nafter\n", Run.Program(source));
    }

    [Fact]
    public void ReturnLeavesTheLoopAndTheFunction()
    {
        const string source = """
            function int first_over list int numbers int limit block
                for int n in numbers if greater n limit return n
                return -1
            end
            write_line first_over list_of int 1 5 9 end 4
            write_line first_over list_of int 1 2 end 4
            write_line first_over empty list int 4
            """;

        Assert.Equal("5\n-1\n-1\n", Run.Program(source));
    }

    [Fact]
    public void ReturnVoidLeavesTheLoopInAVoidFunction()
    {
        const string source = """
            function void show list string names
                for string name in names block
                    if equal name "stop" return void
                    write_line name
                end
            show list_of string "a" "b" "stop" "c" end
            write_line "end"
            """;

        Assert.Equal("a\nb\nend\n", Run.Program(source));
    }

    [Fact]
    public void AForNeverCountsAsAlwaysReturning()
    {
        var error = Failure("function int f list int xs for int x in xs return x", ErrorStage.Type);

        Assert.Contains("must return a value of type int on every path", error.Message);
    }

    [Fact]
    public void ABreakInAForDoesNotStopAnOuterWhileTrueFromAlwaysReturning()
    {
        const string source = """
            function int f list int xs
                while true block
                    for int x in xs if equal x 0 break
                    return 1
                end
            write_line f list_of int 0 end
            """;

        Assert.Equal("1\n", Run.Program(source));
    }

    [Fact]
    public void ABodyThatChangesTheListDoesNotChangeTheIteration()
    {
        const string source = """
            def list int numbers list_of int 1 2 3 end
            for int n in numbers block
                set numbers push numbers multiply n 10
                set numbers remove_at numbers 0
                write_line n
            end
            write_line numbers
            """;

        Assert.Equal("1\n2\n3\n[10, 20, 30]\n", Run.Program(source));
    }

    [Fact]
    public void TheLoopVariableCanBeSetForTheRestOfItsIteration()
    {
        const string source = """
            def list int numbers list_of int 1 2 end
            for int n in numbers block
                set n multiply n 100
                write_line n
            end
            write_line numbers
            """;

        Assert.Equal("100\n200\n[1, 2]\n", Run.Program(source));
    }

    [Fact]
    public void TheLoopVariableIsFreshInEachIteration()
    {
        const string source = """
            for int n in list_of int 1 2 3 end block
                def int doubled multiply n 2
                write_line doubled
            end
            """;

        Assert.Equal("2\n4\n6\n", Run.Program(source));
    }

    [Fact]
    public void TheLoopVariableShadowsAnOuterVariableOnlyInsideTheLoop()
    {
        const string source = """
            def int n 7
            block
                for int n in list_of int 1 2 end write_line n
                write_line n
            end
            write_line n
            """;

        Assert.Equal("1\n2\n7\n7\n", Run.Program(source));
    }

    [Fact]
    public void AFunctionReadsTheGlobalThatALoopVariableHides()
    {
        const string source = """
            def int g 7
            function void show write_line g
            block for int g in list_of int 1 2 end block show write_line g end end
            """;

        Assert.Equal("7\n1\n7\n2\n", Run.Program(source));
    }

    [Fact]
    public void WalksTheResultOfAUserFunction()
    {
        const string source = """
            function list int squares_up_to int n block
                def list int result empty list int
                def int i 1
                while at_most i n block
                    set result push result multiply i i
                    set i add i 1
                end
                return result
            end
            for int square in squares_up_to 3 write_line square
            """;

        Assert.Equal("1\n4\n9\n", Run.Program(source));
    }

    [Fact]
    public void ALoopVariableInADeeperBlockMayShadowAParameter()
    {
        const string source = """
            function void f int n block
                if true for int n in list_of int 1 2 end write_line n
                block for int n in list_of int 3 end write_line n end
                write_line n
            end
            f 9
            """;

        Assert.Equal("1\n2\n3\n9\n", Run.Program(source));
    }

    [Theory]
    [InlineData("def int g 7\nfor int g in list_of int 1 2 end write_line g", "Loop variable 'g' would shadow the variable of the same name declared earlier in this block.", 2)]
    [InlineData("block\ndef int x 1\nfor int x in list_of int 1 end write_line x\nend", "Loop variable 'x' would shadow the variable of the same name declared earlier in this block.", 3)]
    [InlineData("function void f list int x\nfor int x in x write_line x", "Loop variable 'x' would shadow the parameter of the same name.", 2)]
    [InlineData("function void f int x\nfor int x in list_of int 1 end write_line x", "Loop variable 'x' would shadow the parameter of the same name.", 2)]
    [InlineData("function void f int x block\nfor int x in list_of int 1 end write_line x\nend", "Loop variable 'x' would shadow the parameter of the same name.", 2)]
    [InlineData("function int f int x block\ndef int y 0\nfor int y in list_of int 1 end write_line y\nreturn x\nend", "Loop variable 'y' would shadow the variable of the same name declared earlier in this block.", 3)]
    public void ALoopVariableCannotShadowANameOfItsOwnStatementList(string source, string message, int line)
    {
        var error = Failure(source, ErrorStage.Parse);

        Assert.Equal(message, error.Message);
        Assert.Equal(line, error.Position.Line);
        Assert.Equal(9, error.Position.Column);
    }

    [Fact]
    public void ADefAfterTheLoopMayReuseTheLoopVariableName()
    {
        Assert.Equal("1\n5\n", Run.Program("for int n in list_of int 1 end write_line n\ndef int n 5\nwrite_line n"));
    }

    [Fact]
    public void AnInnerForMayReuseTheOuterLoopVariableName()
    {
        const string source = """
            for int n in list_of int 1 2 end block
                for int n in list_of int 10 end write_line n
                write_line n
            end
            """;

        Assert.Equal("10\n1\n10\n2\n", Run.Program(source));
    }

    [Fact]
    public void ABlockNestedDeeperMayShadowTheLoopVariable()
    {
        const string source = """
            for int n in list_of int 1 end block
                block
                    def int n 5
                    write_line n
                end
                write_line n
            end
            """;

        Assert.Equal("5\n1\n", Run.Program(source));
    }

    [Fact]
    public void TheLoopVariableIsGoneAfterTheLoop()
    {
        var error = Failure("for int n in list_of int 1 end write_line n\nwrite_line n", ErrorStage.Type);

        Assert.Equal("Undeclared variable 'n'.", error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void TheListIsEvaluatedInTheEnclosingScope()
    {
        var error = Failure("for list int n in list_of list int n end write_line n", ErrorStage.Type);

        Assert.Equal("Undeclared variable 'n'.", error.Message);
    }

    [Fact]
    public void ADefOfTheLoopVariableInTheOutermostBodyBlockIsAParseError()
    {
        var error = Failure("for int n in list_of int 1 end block\ndef int n 2\nend", ErrorStage.Parse);

        Assert.Equal("Variable 'n' would shadow the loop variable of the same name.", error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void TheBodyCannotBeABareDef()
    {
        var error = Failure("for int n in list_of int 1 end def int m n", ErrorStage.Parse);

        Assert.Equal("A def cannot be the body of a for loop; wrap it in block … end.", error.Message);
    }

    [Fact]
    public void TheLoopVariableTypeMustEqualTheElementType()
    {
        var error = Failure("for float x in list_of int 1 2 end write_line x", ErrorStage.Type);

        Assert.Equal("The loop variable 'x' is float, but the list holds int items.", error.Message);
        Assert.Equal(16, error.Position.Column);
    }

    [Fact]
    public void TheLoopVariableTypeMustMatchNestedElementTypesExactly()
    {
        var error = Failure("def list list int grid empty list list int\nfor list float row in grid write_line row", ErrorStage.Type);

        Assert.Equal("The loop variable 'row' is list float, but the list holds list int items.", error.Message);
    }

    [Fact]
    public void WalkingAMapPointsToKeysOf()
    {
        var error = Failure("def map string int m empty map string int\nfor string k in m write_line k", ErrorStage.Type);

        Assert.Equal("'for' walks a list, found map string int. Walk a map's keys with keys_of.", error.Message);
        Assert.Equal(2, error.Position.Line);
        Assert.Equal(17, error.Position.Column);
    }

    [Fact]
    public void WalkingSomethingOtherThanAListIsATypeError()
    {
        var error = Failure("for string c in \"abc\" write_line c", ErrorStage.Type);

        Assert.Equal("'for' walks a list, found string.", error.Message);
    }

    [Fact]
    public void TheBodyIsCheckedWithTheLoopVariableType()
    {
        var error = Failure("for int n in list_of int 1 end write_line add n \"a\"", ErrorStage.Type);

        Assert.Contains("'add' cannot take int string", error.Message);
    }

    [Fact]
    public void ReportsErrorsInTheListAndTheBodyTogether()
    {
        var errors = Run.Failures("for int n in list_of int \"a\" end write_line add n true");

        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData("for int n list_of int 1 end write_line n", "Expected 'in' after the loop variable: for int n in <list> <statement>, found 'list_of'.")]
    [InlineData("for n in list_of int 1 end write_line n", "Expected a type (")]
    [InlineData("for void n in list_of int 1 end write_line n", "A loop variable cannot have type void.")]
    [InlineData("for int write_line in list_of int 1 end write_line 1", "'write_line' is a reserved word and cannot be used as a loop variable name.")]
    [InlineData("for int in in list_of int 1 end write_line 1", "'in' is a reserved word and cannot be used as a loop variable name.")]
    [InlineData("function void f write_line 1\nfor int f in list_of int 1 end write_line 1", "'f' is a function and cannot be used as a loop variable name.")]
    [InlineData("in", "'in' without a matching 'for'.")]
    [InlineData("def int for 1", "'for' is a reserved word and cannot be used as a variable name.")]
    [InlineData("def int in 1", "'in' is a reserved word and cannot be used as a variable name.")]
    [InlineData("function void for write_line 1", "reserved word")]
    [InlineData("def int x for", "needs an initial value")]
    [InlineData("for int n in list_of int 1 end function void f write_line 1", "Functions can only be defined at the top level.")]
    [InlineData("for int n in list_of int 1 end use m \"m.prp\"", "'use' is only allowed at the top level.")]
    public void ReportsParseErrors(string source, string message)
    {
        var error = Failure(source, ErrorStage.Parse);

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void BreakInAFunctionCalledFromAForIsAParseError()
    {
        var error = Failure("function void f break\nfor int n in list_of int 1 end f", ErrorStage.Parse);

        Assert.Equal("'break' is only allowed inside a while or for loop.", error.Message);
    }

    [Theory]
    [InlineData("for")]
    [InlineData("for int")]
    [InlineData("for list")]
    [InlineData("for int n")]
    [InlineData("for int n in")]
    [InlineData("for int n in numbers")]
    [InlineData("for int n in list_of int 1 2")]
    [InlineData("for int n in numbers block write_line n")]
    public void AnUnfinishedForAsksForMoreInput(string source)
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run("def list int numbers list_of int 1 end");

        Assert.Equal(InputStatus.Incomplete, session.Probe(source));
    }

    [Fact]
    public void AForEndingInAnOpenIfWaitsForElseInTheRepl()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());

        Assert.Equal(InputStatus.AwaitingElse, session.Probe("for int n in list_of int 1 end if equal n 1 write_line n"));
    }

    [Fact]
    public void WorksAcrossReplInputs()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);
        session.Run("def list string names list_of string \"a\" \"b\" end");
        session.Run("for string name in names write_line name");
        session.Run("for string name in push names \"c\" write name");

        Assert.Equal("a\nb\nabc", output.ToString());
    }

    [Fact]
    public void PrintsForLoopsInTheTree()
    {
        const string source = """
            def map string int prices empty map string int
            for string name in keys_of prices block
                if equal name "x" continue
                write_line name
            end
            for int n in list_of int 1 2 end write_line n
            for list int row in empty list list int
                for int cell in row break
            """;

        Assert.Equal(
            """
            def map string int prices (empty map string int)
            for string name in (keys_of prices) block
                if (equal name "x")
                    continue
                write_line name
            end
            for int n in (list_of int 1 2 end)
                write_line n
            for list int row in (empty list list int)
                for int cell in row
                    break

            """.ReplaceLineEndings("\n"),
            Tree(source));
    }

    [Fact]
    public void WalksALargeListQuickly()
    {
        const string source = """
            def list int numbers empty list int
            def int i 0
            while less i 100000 block
                set numbers push numbers i
                set i add i 1
            end
            def int total 0
            for int n in numbers set total add total n
            write_line total
            """;

        var watch = Stopwatch.StartNew();
        var output = Run.Program(source);
        watch.Stop();

        Assert.Equal("4999950000\n", output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"Took {watch.Elapsed}.");
    }
}
