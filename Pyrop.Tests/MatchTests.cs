namespace Pyrop.Tests;

public class MatchTests
{
    private const string Action = "choice Action go int take string wait end\n";

    private static PyropError Failure(string source, ErrorStage stage, int line, int column)
    {
        var error = Run.Failure(source);
        Assert.Equal(stage, error.Stage);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
        return error;
    }

    [Fact]
    public void VariantsCarryData()
    {
        const string source = """
            record Point int x int y
            choice Shape circle float rect float float dot Point empty_shape end
            def list Shape shapes list_of Shape circle 1.5 rect 2.0 3.0 dot Point 1 2 empty_shape end
            write_line shapes
            write_line get shapes 1
            write_line convert string get shapes 2
            write_line some circle 1.0
            record Holder Shape shape
            write_line Holder rect 1.0 2.0
            write_line Holder empty_shape
            write_line list_of string "a" end
            choice Note said string end
            write_line list_of Note said "hi \"you\"" end
            """;

        Assert.Equal(
            "[circle 1.5, rect 2.0 3.0, dot (Point x 1 y 2), empty_shape]\n" +
            "rect 2.0 3.0\n" +
            "dot (Point x 1 y 2)\n" +
            "some (circle 1.0)\n" +
            "Holder shape (rect 1.0 2.0)\n" +
            "Holder shape empty_shape\n" +
            "[\"a\"]\n" +
            "[said \"hi \\\"you\\\"\"]\n",
            Run.Program(source));
    }

    [Fact]
    public void VariantsWithDataCompareStructurally()
    {
        const string source = Action + """
            write_line equal go 1 go 1
            write_line equal go 1 go 2
            write_line equal go 1 wait
            write_line equal take "a" take "a"
            write_line has list_of Action go 1 wait end go 1
            write_line has list_of Action go 1 wait end go 3
            write_line equal some go 1 some go 1
            """;

        Assert.Equal("true\nfalse\nfalse\ntrue\ntrue\nfalse\ntrue\n", Run.Program(source));
    }

    [Fact]
    public void MatchRunsTheBranchOfTheVariantWithItsData()
    {
        const string source = Action + """
            function string describe Action action
                match action
                    when go steps return add "go " convert string steps
                    when take what return add "take " what
                    when wait return "wait"
                end

            for Action action in list_of Action go 3 take "lamp" wait end write_line describe action
            choice Pair both int string end
            match both 7 "seven"
                when both number name block
                    write_line number
                    write_line name
                end
            end
            """;

        Assert.Equal("go 3\ntake lamp\nwait\n7\nseven\n", Run.Program(source));
    }

    [Fact]
    public void ChoicesMayHoldThemselves()
    {
        const string source = """
            choice Tree leaf int node Tree Tree end
            function int total Tree tree
                match tree
                    when leaf n return n
                    when node left right return add total left total right
                end

            def Tree tree node leaf 1 node leaf 2 leaf 3
            write_line total tree
            write_line tree
            choice Chain stop link int Chain end
            write_line link 1 link 2 stop
            """;

        Assert.Equal("6\nnode (leaf 1) (node (leaf 2) (leaf 3))\nlink 1 (link 2 stop)\n", Run.Program(source));
    }

    [Fact]
    public void MatchWorksWithChoicesWithoutData()
    {
        const string source = """
            choice Direction north east south west end
            function string turn Direction d
                match d
                    when north return "N"
                    when east return "E"
                    when south return "S"
                    when west return "W"
                end

            write_line turn west
            """;

        Assert.Equal("W\n", Run.Program(source));
    }

    [Fact]
    public void BranchesCanBreakContinueAndNest()
    {
        const string source = Action + """
            def list Action actions list_of Action wait go 1 take "x" go 2 end
            for Action action in actions
                match action
                    when go n
                        match go add n 10
                            when go m write_line m
                            when take t continue
                            when wait break
                        end
                    when take t break
                    when wait continue
                end
            write_line "done"
            def int count 0
            while true block
                match go count
                    when go n if equal n 3 break else set count add n 1
                    when take t continue
                    when wait continue
                end
            end
            write_line count
            """;

        Assert.Equal("11\ndone\n3\n", Run.Program(source));
    }

    [Fact]
    public void AMatchReturnsWhenEveryBranchReturns()
    {
        Assert.Equal(
            "1\n",
            Run.Program(Action + "function int f Action a\n    match a\n        when go n return n\n        when take t return 0\n        when wait exit 0\n    end\nwrite_line f go 1"));

        var error = Failure(
            Action + "function int f Action a\n    match a\n        when go n return n\n        when take t write_line t\n        when wait return 0\n    end",
            ErrorStage.Type, 2, 14);
        Assert.Equal("Function 'f' must return a value of type int on every path.", error.Message);
    }

    [Fact]
    public void ABranchValueExistsOnlyInItsBranch()
    {
        var error = Failure(Action + "match go 1\n    when go n write_line n\n    when take t write_line n\n    when wait write_line 0\nend", ErrorStage.Type, 4, 28);
        Assert.Equal("Undeclared variable 'n'.", error.Message);

        Assert.Equal("1\n1\n", Run.Program(Action + "match go 1\n    when go n block\n        set n add n 0\n        write_line n\n    end\n    when take t write_line t\n    when wait write_line 0\nend\ndef int n 1\nwrite_line n"));
    }

    [Theory]
    [InlineData("match wait\n    when go n write_line n\n    when take t write_line t\nend", 2, 1,
        "This match does not handle the variant wait of Action; a match needs a when for every variant.")]
    [InlineData("match wait\n    when wait write_line 0\nend", 2, 1,
        "This match does not handle the variants go and take of Action; a match needs a when for every variant.")]
    [InlineData("match 5\n    when wait write_line 0\nend", 2, 7, "'match' takes a choice, found int.")]
    [InlineData("choice Other other end\nmatch wait\n    when go n write_line n\n    when take t write_line t\n    when wait write_line 0\n    when other write_line 0\nend", 7, 5,
        "'other' is a variant of Other, but this match is over Action.")]
    [InlineData("match wait\n    when go n write_line n\n    when take t write_line t\n    when wait write_line 0\n    when wait write_line 1\nend", 6, 5,
        "The variant 'wait' is handled twice in this match.")]
    [InlineData("match wait\n    when go n write_line add n \"x\"\n    when take t write_line t\n    when wait write_line 0\nend", 3, 26,
        "'add' cannot take int string. It accepts: int int, float float, string string.")]
    [InlineData("write_line go \"x\"", 2, 15, "The variant 'go' carries int, found string.")]
    [InlineData("choice Pair both int string end\nwrite_line both 1 2", 3, 19, "Value 2 of the variant 'both' must be string, found int.")]
    public void MatchAndVariantTypeErrors(string body, int line, int column, string message)
    {
        var error = Failure(Action + body, ErrorStage.Type, line, column);
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("match wait\n    when go write_line 1\nend", 3, 13,
        "The variant 'go' carries a value (int), so 'when go' needs a name for it, as in when go value …; found 'write_line'.")]
    [InlineData("choice Pair both int string end\nmatch both 1 \"a\"\n    when both n end", 4, 17,
        "The variant 'both' carries 2 values (int string), so 'when both' needs a name for each, as in when both value1 value2 …; found 'end'.")]
    [InlineData("choice Pair both int string end\nmatch both 1 \"a\"\n    when both n n write_line n\nend", 4, 17, "The when value 'n' is named twice.")]
    [InlineData("match wait\n    when go add write_line 1\nend", 3, 13, "'add' is a reserved word and cannot be used as a when value name.")]
    [InlineData("def int n 1\nmatch wait\n    when go n write_line n\nend", 4, 13,
        "The when value 'n' would shadow the variable of the same name declared earlier in this block.")]
    [InlineData("function void f int n\n    match wait\n        when go n write_line n\n        when take t write_line t\n        when wait write_line 0\n    end", 4, 17,
        "The when value 'n' would shadow the parameter of the same name.")]
    [InlineData("match go 1\n    when go n block\n        def int n 2\n    end\nend", 4, 17, "Variable 'n' would shadow the when value of the same name.")]
    [InlineData("match wait\n    write_line 1\nend", 3, 5, "Expected 'when' or 'end' to close the match started at line 2, column 1, found 'write_line'.")]
    [InlineData("match wait end", 2, 12, "'match' needs a branch for each variant before 'end', such as when <variant> <statement>.")]
    [InlineData("match wait\n    when Action write_line 1\nend", 3, 10, "'Action' is a choice; 'when' names one of its variants, such as when go ….")]
    [InlineData("match wait\n    when nothing write_line 1\nend", 3, 10, "Expected a variant after 'when', found 'nothing'.")]
    [InlineData("when wait write_line 1", 2, 1, "'when' without a matching 'match'.")]
    [InlineData("go 1", 2, 1,
        "A variant that carries data cannot be a statement: 'go …' would make a value only to throw it away. Use it as a value, as in set <name> go ….")]
    [InlineData("def map Action int m map_of Action int end", 2, 9,
        "The choice 'Action' cannot be a map key, because some of its variants carry data; only a choice whose variants carry none can.")]
    [InlineData("def int match 1", 2, 9, "'match' is a reserved word and cannot be used as a variable name.")]
    [InlineData("def int when 1", 2, 9, "'when' is a reserved word and cannot be used as a variable name.")]
    [InlineData("match wait\n    when wait def int x 1\nend", 3, 15, "A def cannot be the body of a when; wrap it in block … end.")]
    [InlineData("def Action a Action", 2, 14, "'Action' is a choice, not a value; name one of its variants, such as go ….")]
    public void MatchAndVariantParseErrors(string body, int line, int column, string message)
    {
        var error = Failure(Action + body, ErrorStage.Parse, line, column);
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("choice C more C end", 1, 8,
        "Choice 'C' contains itself: every variant carries data that cannot be made without a C, so no C could ever be made. Give it a variant that does not need one, such as a variant without data.")]
    [InlineData("record R C c\nchoice C wrap R end", 2, 8,
        "Choice 'C' contains itself: every variant carries data that cannot be made without a C, so no C could ever be made. Give it a variant that does not need one, such as a variant without data.")]
    [InlineData("choice D a void end", 1, 12, "The variant 'a' cannot carry void; void has no values.")]
    [InlineData("choice D void end", 1, 10, "'void' is a reserved word and cannot be used as a variant name.")]
    [InlineData("choice D a option void end", 1, 19, "A type argument cannot have type void.")]
    [InlineData("choice D a int b end\nchoice E c end\nmatch c\n    when D write_line 1\nend", 4, 10, "'D' is a choice; 'when' names one of its variants, such as when a ….")]
    [InlineData("choice D a list end", 1, 17,
        "Expected a type (int, float, string, bool, list, map, option, a record or choice name), found 'end'.")]
    [InlineData("choice D a int\nwrite_line 1", 2, 1, "Expected a variant name or 'end' to close the choice started at line 1, column 1, found 'write_line'.")]
    public void DeclarationErrors(string source, int line, int column, string message)
    {
        var error = Failure(source, ErrorStage.Parse, line, column);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void DataMayUseAnyTypeAndChoicesMayReferToEachOther()
    {
        const string source = """
            choice Event moved Place list int noted map string option Event quiet end
            choice Place inside outside end
            def Event e moved outside list_of int 1 2 end
            write_line e
            write_line noted map_of string option Event "x" some quiet "y" none Event end
            """;

        Assert.Equal("moved outside [1, 2]\nnoted {\"x\": some quiet, \"y\": none}\n", Run.Program(source));
    }

    [Fact]
    public void ChoicesWithDataAreKeptAcrossReplInputs()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        session.Run(Action);
        session.Run(Action + "def Action a go 4");
        session.Run("match a\n    when go n write_line n\n    when take t write_line t\n    when wait write_line 0\nend");
        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run("choice Action go float take string wait end")).Errors);

        Assert.Equal("4\n", output.ToString());
        Assert.Equal(
            "Choice 'Action' was declared earlier in this session as choice Action go int take string wait end, and cannot be declared again with other variants.",
            error.Message);
    }

    [Fact]
    public void AnUnfinishedMatchIsIncompleteInput()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run(Action);

        Assert.Equal(InputStatus.Incomplete, session.Probe("match wait"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("match wait\n    when go"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("match wait\n    when go n write_line n"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("write_line go"));
        Assert.Equal(InputStatus.Ready, session.Probe("match wait\n    when go n write_line n\n    when take t write_line t\n    when wait write_line 0\nend"));
    }

    [Fact]
    public void VariantsNestedBeyondTheLimitAreRuntimeErrors()
    {
        const string declarations = "choice Tree leaf node Tree end\ndef Tree a node node node node leaf\ndef Tree b node node node node leaf\n";
        var session = new PyropSession(new StringReader(""), new StringOutput()) { NestingLimit = 3 };

        var equal = Assert.Single(Assert.Throws<PyropException>(() => session.Run(declarations + "write_line equal a b")).Errors);
        var text = Assert.Single(Assert.Throws<PyropException>(() => session.Run(declarations + "write_line a")).Errors);

        Assert.Equal("'equal' cannot compare values nested this deeply.", equal.Message);
        Assert.Equal("This value is nested too deeply to be written as text.", text.Message);
    }

    [Fact]
    public void PrintsVariantsAndMatchesInTheTree()
    {
        const string source = Action + """
            match go add 1 2
                when go n write_line n
                when take t block
                    write_line t
                end
                when wait write_line take "x"
            end
            """;
        var inspected = new PyropSession(new StringReader(""), new StringOutput()).Inspect(source, "main.prp");

        Assert.Null(inspected.Error);
        Assert.Equal(
            "choice Action go int take string wait end\n" +
            "match (go (add 1 2))\n" +
            "    when go n\n" +
            "        write_line n\n" +
            "    when take t block\n" +
            "        write_line t\n" +
            "    end\n" +
            "    when wait\n" +
            "        write_line (take \"x\")\n" +
            "end\n",
            inspected.Text);
    }
}
