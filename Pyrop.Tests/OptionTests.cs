namespace Pyrop.Tests;

public class OptionTests
{
    private static PyropError Failure(string source, ErrorStage stage, int line, int column, string input = "")
    {
        var error = Run.Failure(source, input);
        Assert.Equal(stage, error.Stage);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
        return error;
    }

    private static string Tree(string source)
    {
        var inspected = new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, "main.prp");
        Assert.Null(inspected.Error);
        return inspected.Text!;
    }

    [Fact]
    public void IfSomeUnwrapsAValueOrRunsItsElse()
    {
        const string source = """
            if_some int age attempt convert int read
                write_line add age 1
            else
                write_line "Not a number"
            if_some int age attempt convert int read
                write_line add age 1
            else
                write_line "Not a number"
            if_some string name none string write_line name
            write_line "done"
            """;

        Assert.Equal("42\nNot a number\ndone\n", Run.Program(source, "41\nabc\n"));
    }

    [Fact]
    public void SomeNoneAndValueOrWorkInUserFunctions()
    {
        const string source = """
            record Room string name int size end
            define list Room rooms list_of Room Room "Hall" 10 Room "Cellar" 4 end
            function option Room find_room string wanted
            block
                for Room room in rooms if equal the name of room wanted return some room
                return none Room
            end
            function int size_of option Room room
            block
                if_some Room found room return the size of found
                else return 0
            end
            write_line find_room "Cellar"
            write_line find_room "Attic"
            write_line size_of find_room "Hall"
            write_line size_of find_room "Attic"
            write_line value_or find_room "Attic" Room "Nowhere" 0
            """;

        Assert.Equal(
            "some (Room name \"Cellar\" size 4)\nnone\n10\n0\nRoom name \"Nowhere\" size 0\n",
            Run.Program(source));
    }

    [Fact]
    public void ValueOrEvaluatesItsDefaultOnlyWhenTheOptionIsAbsent()
    {
        const string source = """
            write_line value_or some 1 convert int read
            write_line value_or none int convert int read
            """;

        Assert.Equal("1\n7\n", Run.Program(source, "7\n"));

        var error = Failure("write_line value_or some 1 2.0", ErrorStage.Type, 1, 12);
        Assert.StartsWith("'value_or' cannot take option int float.", error.Message);
    }

    [Fact]
    public void OptionsNestInCollectionsRecordsAndOtherOptions()
    {
        const string source = """
            record Node int value option Node next end
            define Node chain Node 1 some Node 2 some Node 3 none Node
            write_line chain
            define list option string names list_of option string some "Ann" none string some "a \"b\"" end
            write_line names
            define map string option int ages put put map_of string option int end "Ann" some 30 "Bob" none int
            write_line ages
            define option option int twice some some 2
            write_line twice
            write_line some none int
            write_line none option int
            write_line some list_of int 1 2 end
            write_line convert string some "Ann"
            write_line some "Ann"
            define int total 0
            define option Node current some chain
            define bool walking true
            while walking block
                if_some Node node current block
                    set total add total the value of node
                    set current the next of node
                end
                else set walking false
            end
            write_line total
            """;

        Assert.Equal(
            "Node value 1 next some (Node value 2 next some (Node value 3 next none))\n" +
            "[some \"Ann\", none, some \"a \\\"b\\\"\"]\n" +
            "{\"Ann\": some 30, \"Bob\": none}\n" +
            "some some 2\nsome none\nnone\nsome [1, 2]\nsome \"Ann\"\nsome \"Ann\"\n6\n",
            Run.Program(source));
    }

    [Fact]
    public void OptionsCompareStructurally()
    {
        const string source = """
            write_line equal some 1 some 1
            write_line equal some 1 some 2
            write_line equal some 1 none int
            write_line equal none int none int
            write_line equal some some 1 some none int
            write_line equal some list_of int 1 end some list_of int 1 end
            write_line has list_of option int some 1 none int end none int
            """;

        Assert.Equal("true\nfalse\nfalse\ntrue\nfalse\ntrue\ntrue\n", Run.Program(source));

        var error = Failure("write_line equal some 1 some 1.0", ErrorStage.Type, 1, 12);
        Assert.StartsWith("'equal' cannot take option int option float.", error.Message);
    }

    [Fact]
    public void OptionsConvertOnlyToStringAndThemselves()
    {
        Assert.Equal("none\n", Run.Program("write_line convert option int none int"));

        var error = Failure("write_line convert int some 1", ErrorStage.Type, 1, 12);
        Assert.Equal("Cannot convert option int to int.", error.Message);

        error = Failure("write_line convert option int 1", ErrorStage.Type, 1, 12);
        Assert.Equal("Cannot convert int to option int.", error.Message);
    }

    [Fact]
    public void AnOptionIsNotAMapKey()
    {
        var error = Failure("define map option int int m map_of option int int end", ErrorStage.Parse, 1, 12);
        Assert.Equal("A map key must be int, string, bool or a choice, found option int.", error.Message);
    }

    [Fact]
    public void NoneTakesAValueType()
    {
        var error = Failure("write_line none void", ErrorStage.Parse, 1, 17);
        Assert.Equal("'none' takes the type of the missing value, such as none int; void has no values.", error.Message);

        error = Failure("write_line none 5", ErrorStage.Parse, 1, 17);
        Assert.StartsWith("Expected a type", error.Message);

        var exception = Assert.Throws<PyropException>(() => Run.Program("write_line none"));
        Assert.True(exception.IsIncompleteInput);
    }

    [Fact]
    public void IfSomeChecksTheOptionAndTheValueType()
    {
        var error = Failure("if_some int x 5 write_line x", ErrorStage.Type, 1, 15);
        Assert.Equal("'if_some' unwraps an option, found int.", error.Message);

        error = Failure("if_some float x attempt convert int read write_line x", ErrorStage.Type, 1, 17);
        Assert.Equal("The if_some value 'x' is float, but the option holds int.", error.Message);

        error = Failure("if_some int x some 1 set x \"a\"", ErrorStage.Type, 1, 28);
        Assert.Equal("Cannot set int variable 'x' to a value of type string.", error.Message);
    }

    [Fact]
    public void IfSomeTakesAValueType()
    {
        var error = Failure("if_some void x none int write_line 1", ErrorStage.Parse, 1, 9);
        Assert.Equal("'if_some' takes the type of the value it unwraps, such as if_some int age …; void has no values.", error.Message);
    }

    [Fact]
    public void TheValueExistsOnlyInTheFirstBody()
    {
        var error = Failure("if_some int x none int write_line 1 else write_line x", ErrorStage.Type, 1, 53);
        Assert.Equal("Undeclared variable 'x'.", error.Message);

        error = Failure("if_some int x some 1 write_line x\nwrite_line x", ErrorStage.Type, 2, 12);
        Assert.Equal("Undeclared variable 'x'.", error.Message);

        const string source = """
            if_some int x some 1 block
                set x add x 1
                write_line x
            end
            define int x 10
            write_line x
            """;

        Assert.Equal("2\n10\n", Run.Program(source));
    }

    [Fact]
    public void ElseBindsToTheNearestUnmatchedIfOrIfSome()
    {
        const string ifThenIfSome = """
            if true if_some int x none int write_line x else write_line "if_some else"
            if false if_some int x none int write_line x else write_line "never"
            write_line "end"
            """;

        Assert.Equal("if_some else\nend\n", Run.Program(ifThenIfSome));

        const string ifSomeThenIf = """
            if_some int x some 1 if equal x 2 write_line "two" else write_line "if else"
            if_some int x none int if true write_line "never" else write_line "never"
            write_line "end"
            """;

        Assert.Equal("if else\nend\n", Run.Program(ifSomeThenIf));

        const string chains = """
            function string describe option int a option string b
            block
                if_some int number a return convert string number
                else if_some string text b return text
                else if false return "never"
                else return "nothing"
            end
            write_line describe some 1 some "b"
            write_line describe none int some "b"
            write_line describe none int none string
            if_some int x none int write_line x
            else if_some int y some 2 if_some int z none int write_line z else write_line add y 1
            """;

        Assert.Equal("1\nb\nnothing\n3\n", Run.Program(chains));
    }

    [Fact]
    public void IfSomeElseAlwaysReturnsWhenBothBodiesDo()
    {
        const string returns = """
            function int unwrap option int a
                if_some int x a return x else return 0
            write_line add unwrap some 4 unwrap none int
            """;

        Assert.Equal("4\n", Run.Program(returns));

        var error = Failure("function int unwrap option int a\n    if_some int x a return x", ErrorStage.Type, 1, 14);
        Assert.Equal("Function 'unwrap' must return a value of type int on every path.", error.Message);

        error = Failure("function int unwrap option int a\n    if_some int x a write_line x else return 0", ErrorStage.Type, 1, 14);
        Assert.Equal("Function 'unwrap' must return a value of type int on every path.", error.Message);

        const string loops = """
            function int first list option int items
            block
                for option int item in items if_some int x item return x
                return -1
            end
            function int forever option int a
                while true if_some int x a break else return 1
            write_line first list_of option int none int some 5 some 6 end
            """;

        error = Failure(loops, ErrorStage.Type, 6, 14);
        Assert.Equal("Function 'forever' must return a value of type int on every path.", error.Message);
    }

    [Fact]
    public void IfSomeCompletionsPassThroughLoops()
    {
        const string source = """
            define int total 0
            for option int item in list_of option int some 1 none int some 5 some 100 end block
                if_some int x item block
                    if greater x 50 break
                    set total add total x
                end
                else continue
            end
            write_line total
            """;

        Assert.Equal("6\n", Run.Program(source));
    }

    [Theory]
    [InlineData("define int x 1 if_some int x some 2 write_line x", 1, 28, "The if_some value 'x' would shadow the variable of the same name declared earlier in this block.")]
    [InlineData("function void f int x if_some int x some 2 write_line x", 1, 35, "The if_some value 'x' would shadow the parameter of the same name.")]
    [InlineData("function void f int x block if_some int x some 2 write_line x end", 1, 41, "The if_some value 'x' would shadow the parameter of the same name.")]
    [InlineData("if_some int x some 1 block define int x 2 end", 1, 39, "Variable 'x' would shadow the if_some value of the same name.")]
    [InlineData("if_some int add some 1 write_line add", 1, 13, "'add' is a reserved word and cannot be used as a value name.")]
    [InlineData("function void f return void\nif_some int f some 1 write_line 1", 2, 13, "'f' is a function and cannot be used as a value name.")]
    [InlineData("if_some int x some 1 define int y 1", 1, 22, "A define cannot be the body of an if_some; wrap it in block … end.")]
    [InlineData("if_some int x some 1 write_line x else define int y 1", 1, 40, "A define cannot be the body of an else; wrap it in block … end.")]
    public void IfSomeNamesFollowTheLoopVariableRules(string source, int line, int column, string message)
    {
        var error = Failure(source, ErrorStage.Parse, line, column);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void IfSomeNamesMayShadowWhenNestedDeeper()
    {
        const string source = """
            function void show int x
            block
                if true if_some int x some add x 1 write_line x
                block
                    if_some int x some 5 write_line x
                end
                if true for int y in list_of int 7 end if_some int y some add y 1 write_line y
                write_line x
            end
            show 1
            """;

        Assert.Equal("2\n5\n8\n1\n", Run.Program(source));
    }

    [Fact]
    public void TreePrintsOptionsAttemptAndIfSome()
    {
        const string source = """
            use files library
            import from files load end
            if_some int age attempt convert int read write_line age
            else if_some string text attempt load "notes.txt" write_line text
            else write_line value_or none int 0
            if not attempt from files save "save.txt" "data" write_line some "x"
            """;

        Assert.Equal(
            "use files library\n" +
            "import from files load end\n" +
            "if_some int age (attempt (convert int read))\n" +
            "    write_line age\n" +
            "else if_some string text (attempt (load \"notes.txt\"))\n" +
            "    write_line text\n" +
            "else\n" +
            "    write_line (value_or (none int) 0)\n" +
            "if (not (attempt (from files save \"save.txt\" \"data\")))\n" +
            "    write_line (some \"x\")\n",
            Tree(source));

        Assert.Equal("write_line (attempt read)\n", Tree("write_line attempt read"));
        Assert.Equal("if_some int x (some 1) block\n    write_line x\nend\n", Tree("if_some int x some 1 block write_line x end"));
    }

    [Fact]
    public void TheReplWaitsForAPossibleElseAfterIfSome()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        Assert.Equal(InputStatus.AwaitingElse, session.Probe("if_some int x some 1 write_line x"));
        Assert.Equal(InputStatus.AwaitingElse, session.Probe("if_some int x some 1 write_line x else if_some int y none int write_line y"));
        Assert.Equal(InputStatus.AwaitingElse, session.Probe("for int i in list_of int 1 end if_some int x some i write_line x"));
        Assert.Equal(InputStatus.AwaitingElse, session.Probe("if_some int x some 1 if true write_line x"));
        Assert.Equal(InputStatus.Ready, session.Probe("if_some int x some 1 write_line x else write_line 0"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("if_some"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("if_some int"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("if_some int x"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("if_some int x some 1"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("if_some int x some 1 write_line x else"));
    }

    [Fact]
    public void NewWordsAreReserved()
    {
        Assert.Equal("'none' is a reserved word and cannot be used as a variable name.", Failure("define int none 1", ErrorStage.Parse, 1, 12).Message);
        Assert.Equal("'some' is a reserved word and cannot be used as a variable name.", Failure("define int some 1", ErrorStage.Parse, 1, 12).Message);
        Assert.Equal("'value_or' is a reserved word and cannot be used as a variable name.", Failure("define int value_or 1", ErrorStage.Parse, 1, 12).Message);
        Assert.Equal("'if_some' is a reserved word and cannot be used as a variable name.", Failure("define int if_some 1", ErrorStage.Parse, 1, 12).Message);
        Assert.Equal("'option' is a reserved word and cannot be used as a variable name.", Failure("define int option 1", ErrorStage.Parse, 1, 12).Message);
        Assert.Equal("'option' is a reserved word and cannot be used as a field name.", Failure("record R int option end", ErrorStage.Parse, 1, 14).Message);
    }

    [Theory]
    [InlineData("if_some int record some 1 write_line 1", 1, 13)]
    [InlineData("if_some record", 1, 9)]
    [InlineData("write_line none record", 1, 17)]
    [InlineData("write_line attempt record", 1, 20)]
    [InlineData("write_line some record", 1, 17)]
    public void RecordAfterTheNewWordsIsReportedAsReserved(string source, int line, int column)
    {
        var error = Failure(source, ErrorStage.Parse, line, column);
        Assert.Equal("'record' is a reserved word and cannot be used as a name.", error.Message);
    }

    [Fact]
    public void ARecordFollowingANoneTypeStillStartsADeclaration()
    {
        const string source = """
            define option int n none int record Point float x float y end
            write_line n
            write_line Point 1.0 2.0
            """;

        Assert.Equal("none\nPoint x 1.0 y 2.0\n", Run.Program(source));
    }

    [Theory]
    [InlineData("is_some some 1", "true")]
    [InlineData("is_some none int", "false")]
    [InlineData("is_none none string", "true")]
    [InlineData("is_none some \"\"", "false")]
    [InlineData("is_some attempt convert int \"x\"", "false")]
    [InlineData("is_some some none int", "true")]
    [InlineData("is_none get list_of option int none int end 0", "true")]
    public void IsSomeAndIsNoneTestForAValue(string expression, string expected)
    {
        Assert.Equal($"{expected}\n", Run.Program($"write_line {expression}"));
    }

    [Fact]
    public void IsSomeTakesOnlyAnOption()
    {
        var error = Failure("write_line is_some 1", ErrorStage.Type, 1, 12);

        Assert.Equal("'is_some' cannot take int. It accepts: option T (T stands for any type, the same wherever it appears).", error.Message);
    }

    [Theory]
    [InlineData("is_some")]
    [InlineData("is_none")]
    public void TheTestsAreReservedWords(string word)
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure($"define int {word} 1").Stage);
    }
}
