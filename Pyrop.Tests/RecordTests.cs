using Pyrop.Runtime;

namespace Pyrop.Tests;

public class RecordTests
{
    private const string Point = "record Point float x float y end\n";

    private static PyropError Failure(string source, ErrorStage stage, int line, int column)
    {
        var error = Run.Failure(source);
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
    public void ConstructsReadsAndChangesFields()
    {
        const string source = """
            record Point float x float y end
            record Hero string name int health Point position list string inventory end
            define Hero hero Hero "Ann" 10 Point 1.5 2.0 list_of string "lamp" end
            write_line the health of hero
            write_line the x of the position of hero
            set hero copy hero with health subtract the health of hero 3 end
            write_line hero
            """;

        Assert.Equal(
            "10\n1.5\nHero name \"Ann\" health 7 position (Point x 1.5 y 2.0) inventory [\"lamp\"]\n",
            Run.Program(source));
    }

    [Theory]
    [InlineData("Point 1.0 -2.5", "Point x 1.0 y -2.5")]
    [InlineData("list_of Point Point 0.0 0.0 Point 1.0 1.0 end", "[Point x 0.0 y 0.0, Point x 1.0 y 1.0]")]
    [InlineData("put map_of string Point end \"home\" Point 0.0 1.0", "{\"home\": Point x 0.0 y 1.0}")]
    [InlineData("Named \"say \\\"hi\\\"\\n\" true", "Named text \"say \\\"hi\\\"\\n\" flag true")]
    [InlineData("Line Point 0.0 0.0 Point 1.0 1.0", "Line start (Point x 0.0 y 0.0) finish (Point x 1.0 y 1.0)")]
    [InlineData("Outer Line Point 0.0 0.0 Point 1.0 1.0 2", "Outer line (Line start (Point x 0.0 y 0.0) finish (Point x 1.0 y 1.0)) count 2")]
    [InlineData("list_of Line Line Point 0.0 0.0 Point 1.0 1.0 end", "[Line start (Point x 0.0 y 0.0) finish (Point x 1.0 y 1.0)]")]
    [InlineData("Box list_of Named Named \"a\" false end map_of int string end", "Box items [Named text \"a\" flag false] lookup {}")]
    [InlineData("convert string Point 3.0 4.0", "Point x 3.0 y 4.0")]
    public void WritesTheTextFormOfRecords(string expression, string expected)
    {
        const string declarations = """
            record Point float x float y end
            record Named string text bool flag end
            record Box list Named items map int string lookup end
            record Line Point start Point finish end
            record Outer Line line int count end

            """;

        Assert.Equal(expected + "\n", Run.Program(declarations + "write_line " + expression));
    }

    [Theory]
    [InlineData("equal Point 1.0 2.0 Point 1.0 2.0", "true")]
    [InlineData("equal Point 1.0 2.0 Point 1.0 2.5", "false")]
    [InlineData("equal Line Point 0.0 0.0 Point 1.0 1.0 Line Point 0.0 0.0 Point 1.0 1.0", "true")]
    [InlineData("equal Line Point 0.0 0.0 Point 1.0 1.0 Line Point 0.0 0.0 Point 1.0 2.0", "false")]
    [InlineData("has list_of Point Point 1.0 2.0 end Point 1.0 2.0", "true")]
    [InlineData("equal list_of Point end list_of Point end", "true")]
    public void ComparesRecordsStructurally(string expression, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program(Point + "record Line Point start Point finish end\nwrite_line " + expression));
    }

    [Fact]
    public void RecordsAreValues()
    {
        const string source = """
            record Point float x float y end
            function Point moved Point p
            block
                set p copy p with x 9.0 end
                return p
            end
            define Point a Point 1.0 2.0
            define Point b a
            set b copy b with y 5.0 end
            define Point c moved a
            write_line a
            write_line b
            write_line c
            """;

        Assert.Equal("Point x 1.0 y 2.0\nPoint x 1.0 y 5.0\nPoint x 9.0 y 2.0\n", Run.Program(source));
    }

    [Fact]
    public void RecordsCanBeUsedAboveTheirDeclarationAndReferToEachOtherInAnyOrder()
    {
        const string source = """
            define Room hall Room "hall" list_of Exit Exit "north" "kitchen" end
            write_line the target of get the exits of hall 0
            function Room make string name return Room name list_of Exit end
            write_line make "attic"
            record Room string name list Exit exits end
            record Exit string direction string target end
            """;

        Assert.Equal("kitchen\nRoom name \"attic\" exits []\n", Run.Program(source));
    }

    [Fact]
    public void ARecordMayHoldItselfInsideAListOrMap()
    {
        const string source = """
            record Tree string label list Tree children map string Tree named end
            define Tree leaf Tree "leaf" list_of Tree end map_of string Tree end
            define Tree root Tree "root" list_of Tree leaf end put map_of string Tree end "only" leaf
            for Tree child in the children of root write_line the label of child
            write_line the label of get the named of root "only"
            """;

        Assert.Equal("leaf\nleaf\n", Run.Program(source));
    }

    [Fact]
    public void ForWalksAListOfRecords()
    {
        const string source = Point + """
            define float total 0.0
            for Point p in list_of Point Point 1.0 2.0 Point 3.0 4.0 end set total add total add the x of p the y of p
            write_line total
            """;

        Assert.Equal("10.0\n", Run.Program(source));
    }

    [Fact]
    public void RecordNamesMayStartWithAnyLetter()
    {
        const string source = """
            record point float x float y end
            record čas int hodiny end
            record _pair point first čas second end
            function point mirror point p return point the y of p the x of p
            define _pair pair _pair mirror point 1.0 2.0 čas 7
            write_line pair
            for point p in list_of point the first of pair end write_line p
            if_some čas c some the second of pair write_line the hodiny of c
            """;

        Assert.Equal("_pair first (point x 2.0 y 1.0) second (čas hodiny 7)\npoint x 2.0 y 1.0\n7\n", Run.Program(source));
    }

    [Theory]
    [InlineData("record room int x end\ndefine room room room 1", 2, 13, "'room' is a record and cannot be used as a variable name.")]
    [InlineData("record room int x end\nfunction void f room room write_line 1", 2, 22, "'room' is a record and cannot be used as a parameter name.")]
    [InlineData("record room int x end\nrecord room int y end", 2, 8, "Record 'room' is already declared.")]
    [InlineData("function int room return 1\nrecord room int x end", 2, 8, "'room' is a function of this file and cannot also name a record.")]
    public void ALowercaseRecordNameClashesLikeAnyOtherName(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(source, ErrorStage.Parse, line, column).Message);
    }

    [Theory]
    [InlineData("record Node int value Node next end", 1, 8, "Record 'Node' contains itself: Node has field 'next' of type Node, so no Node could ever be made. A record can hold itself only inside a list, a map or an option, such as option Node.")]
    [InlineData("record A int c B b end\nrecord B A a end", 1, 8, "Record 'A' contains itself: A has field 'b' of type B, and B has field 'a' of type A, so no A could ever be made. A record can hold itself only inside a list, a map or an option, such as option A.")]
    [InlineData("record Ok int x end\nrecord A B b end\nrecord B C c end\nrecord C Ok ok A a end", 2, 8, "Record 'A' contains itself: A has field 'b' of type B, B has field 'c' of type C, and C has field 'a' of type A, so no A could ever be made. A record can hold itself only inside a list, a map or an option, such as option A.")]
    public void RejectsARecordThatContainsItself(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(source, ErrorStage.Parse, line, column).Message);
    }

    [Theory]
    [InlineData("record add int x end", 1, 1, "'record' is a reserved word and cannot be used as a name.")]
    [InlineData("record 5 int x end", 1, 1, "'record' is a reserved word and cannot be used as a name.")]
    [InlineData("record P int x end\nrecord P int y end", 2, 8, "Record 'P' is already declared.")]
    [InlineData("record P int x float x end", 1, 22, "Field 'x' is declared twice in record 'P'.")]
    [InlineData("record P int write_line end", 1, 14, "'write_line' is a reserved word and cannot be used as a field name.")]
    [InlineData("record P int from end", 1, 14, "'from' is a reserved word and cannot be used as a field name.")]
    [InlineData("record P void x end", 1, 10, "A field cannot have type void.")]
    [InlineData("record P end", 1, 10, "Record 'P' needs at least one field, a type and a name such as record P int count end.")]
    [InlineData("record P write_line 1", 1, 10, "Expected a field type or 'end' to close the record 'P', found 'write_line'. A record lists its fields as types and names: record P int count string label end.")]
    [InlineData("record P int 5 end", 1, 14, "Expected a field name after the type int, found '5'.")]
    [InlineData("block record P int x end", 1, 7, "Records can only be declared at the top level.")]
    [InlineData("function void f record P int x end", 1, 17, "Records can only be declared at the top level.")]
    [InlineData("function int P return 1\nrecord P int x end", 2, 8, "'P' is a function of this file and cannot also name a record.")]
    [InlineData("record P int x end\nfunction int P return 1", 1, 8, "'P' is a function of this file and cannot also name a record.")]
    [InlineData("record P int x end\ndefine int P 1", 2, 12, "'P' is a record and cannot be used as a variable name.")]
    [InlineData("record P int x end\nfunction void f int P write_line P", 2, 21, "'P' is a record and cannot be used as a parameter name.")]
    [InlineData("record P int x end\nfor int P in list_of int end write_line 1", 2, 9, "'P' is a record and cannot be used as a loop variable name.")]
    [InlineData("record P int x end\ndefine map P int m map_of P int end", 2, 12, "A map key must be int, string, bool or a choice, found P.")]
    [InlineData("define Q q 1", 1, 8, "'Q' is not a type; no record or choice named 'Q' is declared or imported in this file.")]
    [InlineData("function Q f return 1", 1, 10, "'Q' is not a type; no record or choice named 'Q' is declared or imported in this file.")]
    [InlineData("define int x the 5 of y", 1, 18, "Expected a field name after 'the', found '5'.")]
    [InlineData("define int x the add of y", 1, 18, "'add' is a reserved word and cannot be used as a field name.")]
    [InlineData("the x of y", 1, 1, "'the' cannot start a statement.")]
    public void RejectsInvalidRecordDeclarationsAndForms(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(source, ErrorStage.Parse, line, column).Message);
    }

    [Theory]
    [InlineData("define int record 1", 1, 12, "record")]
    [InlineData("write_line record", 1, 12, "record")]
    [InlineData("define string record \"Point\"", 1, 15, "record")]
    [InlineData("set record Point", 1, 5, "record")]
    [InlineData("define Point p Point 1.0 2.0\nwrite_line the record of p", 2, 16, "record")]
    [InlineData("for int record in list_of int end write_line 1", 1, 9, "record")]
    [InlineData("define string answer \"x\"\ndefine string record answer\ndefine int x 1", 2, 15, "record")]
    [InlineData("write_line record\nwrite_line Point 1.0 2.0", 1, 12, "record")]
    [InlineData("define list int l list_of int end\nwrite_line function\nf l", 2, 12, "function")]
    [InlineData("define int function 1", 1, 12, "function")]
    [InlineData("write_line function", 1, 12, "function")]
    [InlineData("set function Point", 1, 5, "function")]
    public void AKeywordThatCannotStartADeclarationIsReportedAsAReservedName(string source, int line, int column, string keyword)
    {
        var program = Point + source;
        var error = Failure(program, ErrorStage.Parse, line + 1, column);

        Assert.Equal($"'{keyword}' is a reserved word and cannot be used as a name.", error.Message);
        Assert.Equal(InputStatus.Ready, new PyropSession(new StringReader(""), new StringOutput()).Probe(program));
    }

    [Fact]
    public void AConstructorCannotBeAStatement()
    {
        var error = Failure("record P int x end\nwrite_line 1\nP 1", ErrorStage.Parse, 3, 1);

        Assert.Equal(
            "A record constructor cannot be a statement: 'P …' would make a record only to throw it away, and no statement may start with a type name, so that a parameter list knows where it ends. Use the new record as a value, as in define P <name> P ….",
            error.Message);
    }

    [Theory]
    [InlineData("record P int x end\nfunction void f int a P 1", 2, 25, "parameter")]
    public void AConstructorAfterAParameterListIsReadAsOneMoreType(string source, int line, int column, string kind)
    {
        var error = Failure(source, ErrorStage.Parse, line, column);

        Assert.Equal(
            $"Expected a {kind} name after the type P, found '1'. No statement can start with a record name, so 'P' was read as the type of one more {kind}; a record constructor cannot be a statement.",
            error.Message);
    }

    [Theory]
    [InlineData("define Point p Point 1 2.0", 2, 22, "Field 'x' of 'Point' must be float, found int.")]
    [InlineData("define Point p Point 1.0 2.0\nwrite_line the z of p", 3, 16, "Record 'Point' has no field 'z'; its fields are x and y.")]
    [InlineData("record One int only end\nwrite_line the z of One 1", 3, 16, "Record 'One' has no field 'z'; its field is only.")]
    [InlineData("write_line the x of 5", 2, 21, "'the … of' takes a record, found int.")]
    [InlineData("write_line copy list_of int end with x 1.0 end", 2, 17, "'copy' takes a record, found list int.")]
    [InlineData("define Point p Point 1.0 2.0\nset p copy p with x \"far\" end", 3, 21, "Cannot set float field 'x' to a value of type string.")]
    [InlineData("define Point p Point 1.0 2.0\ndefine float x p", 3, 16, "Cannot initialize float variable 'x' with a value of type Point.")]
    [InlineData("define Point p Point 1.0 2.0\ndefine int n convert int p", 3, 14, "Cannot convert Point to int.")]
    [InlineData("record Size float x float y end\nwrite_line equal Point 1.0 2.0 Size 1.0 2.0", 3, 12, "'equal' cannot take Point Size. It accepts: T T (T stands for any type, the same wherever it appears).")]
    public void ChecksRecordTypes(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(Point + source, ErrorStage.Type, line, column).Message);
    }

    [Fact]
    public void ReportsEveryWrongFieldAccessOnce()
    {
        var errors = Run.Failures(Point + "define Point p Point 1.0 2.0\nset p copy p with z add 1 true end");

        Assert.Equal(
            ["Record 'Point' has no field 'z'; its fields are x and y.", "'add' cannot take int bool. It accepts: int int, float float, string string."],
            errors.Select(error => error.Message));
    }

    [Fact]
    public void PrintsRecordsInTheTree()
    {
        const string source = """
            export record Point float x float y end
            record Hero string name Point position list Point trail end
            define Hero hero Hero "Ann" Point 0.0 0.0 list_of Point end
            set hero copy hero with position Point the x of the position of hero 1.0 end
            function Point at Hero h return the position of h
            """;

        Assert.Equal(
            "export record Point float x float y end\n" +
            "record Hero string name Point position list Point trail end\n" +
            "define Hero hero (Hero \"Ann\" (Point 0.0 0.0) (list_of Point end))\n" +
            "set hero (copy hero with position (Point (the x of (the position of hero)) 1.0) end)\n" +
            "function Point at Hero h\n    return (the position of h)\n",
            Tree(source));
    }

    [Fact]
    public void ARecordWithoutItsEndIsIncompleteInput()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());

        Assert.Equal(InputStatus.Incomplete, session.Probe("record Point float x"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("export record Point float x"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("record Point"));
        Assert.Equal(InputStatus.Ready, session.Probe("record"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("record Point float"));
        Assert.Equal(InputStatus.Ready, session.Probe("record Point float x end"));
        Assert.Equal(InputStatus.Ready, session.Probe("record Point float x end\nwrite_line 1"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("record Point float x end\nwrite_line the x of"));
    }

    [Fact]
    public void RecordsPersistAcrossReplInputs()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        session.Run("record Point float x float y end");
        session.Run("define Point p Point 1.0 2.0");
        session.Run("function float sum Point q return add the x of q the y of q");
        session.Run("record Point float x float y end");
        session.Run("write_line sum p");
        session.Run("write_line equal p Point 1.0 2.0");

        Assert.Equal("3.0\ntrue\n", output.ToString());
    }

    [Fact]
    public void ARecordCannotBeRedeclaredWithOtherFieldsInTheRepl()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run("record Point float x float y end");

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run("record Point float x end")).Errors);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(
            "Record 'Point' was declared earlier in this session as record Point float x float y end, and cannot be declared again with other fields.",
            error.Message);
        Assert.Equal((1, 8), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void AFailedReplInputDeclaresNoRecord()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);
        Assert.Throws<PyropException>(() => session.Run("record Point float x end\nwrite_line add 1 true"));

        session.Run("record Point int x int y end");
        session.Run("write_line Point 1 2");

        Assert.Equal("Point x 1 y 2\n", output.ToString());
    }

    [Theory]
    [InlineData("define int Point 1", "record Point int x end", "'Point' is already a global variable and cannot be used as a record name.")]
    [InlineData("record Point int x end", "function int Point return 1", "'Point' is a record in this file and cannot also be a function name.")]
    [InlineData("record Point int x end", "define int Point 1", "'Point' is a record and cannot be used as a variable name.")]
    public void ReplRecordsClashWithEarlierNames(string first, string second, string message)
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run(first);

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(second)).Errors);

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void TheMainScriptMayExportDeclarationsUsingPrivateRecords()
    {
        const string source = "export record Hero list Stats stats end\nrecord Stats int strength end\nexport function Stats best return Stats 1\nwrite_line best";

        Assert.Equal("Stats strength 1\n", Run.Program(source));
    }

    [Fact]
    public void FieldNamesDoNotClashWithOtherNames()
    {
        const string source = """
            record Item string name int count end
            record Shop list Item name end
            function string name Item item return the name of item
            define int count 3
            write_line name Item "lamp" count
            """;

        Assert.Equal("lamp\n", Run.Program(source));
    }

    [Fact]
    public void ADeeplyNestedTreeCanStillBeComparedAndWrittenNormally()
    {
        const string source = """
            record Tree list Tree kids end
            define Tree t Tree list_of Tree end
            define Tree u Tree list_of Tree end
            define int i 0
            while less i 1000 block
                set t Tree push list_of Tree end t
                set u Tree push list_of Tree end u
                set i add i 1
            end
            write_line equal t u
            write_line length_of convert string t
            """;

        Assert.Equal("true\n12012\n", Run.Program(source));
    }

    [Theory]
    [InlineData("write_line equal a b", "'equal' cannot compare values nested this deeply.")]
    [InlineData("write_line has list_of Tree a end b", "'has' cannot compare values nested this deeply.")]
    [InlineData("write_line a", "This value is nested too deeply to be written as text.")]
    [InlineData("write_error_line a", "This value is nested too deeply to be written as text.")]
    [InlineData("define string s convert string a", "This value is nested too deeply to be written as text.")]
    public void ValuesNestedBeyondTheLimitAreRuntimeErrors(string statement, string message)
    {
        const string declarations = "record Tree list Tree kids end\n" +
            "define Tree a Tree list_of Tree Tree list_of Tree Tree list_of Tree end end end\n" +
            "define Tree b Tree list_of Tree Tree list_of Tree Tree list_of Tree end end end\n";
        var session = new PyropSession(new StringReader(""), new StringOutput()) { NestingLimit = 4 };

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(declarations + statement)).Errors);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal(4, error.Position.Line);
    }

    [Fact]
    public void ValuesWithinTheNestingLimitCompareAndPrint()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { NestingLimit = 6 };

        session.Run(
            "record Tree list Tree kids end\n" +
            "define Tree a Tree list_of Tree Tree list_of Tree Tree list_of Tree end end end\n" +
            "write_line equal a Tree list_of Tree Tree list_of Tree Tree list_of Tree end end end\n" +
            "write_line a");

        Assert.Equal("true\nTree kids [Tree kids [Tree kids []]]\n", output.ToString());
        Assert.Equal(ValueEquality.MaxNesting, new PyropSession(new StringReader(""), new StringOutput()).NestingLimit);
    }

    [Fact]
    public void RecordStatementsDoNotRun()
    {
        Assert.Equal("1\n", Run.Program("write_line 1\nrecord Point float x float y end"));
    }

    [Fact]
    public void FieldFormsNest()
    {
        const string source = """
            record Person string name end
            record Item string label Person owner end
            define Item item Item "lamp" Person "Ann"
            write_line the name of the owner of item
            set item copy item with owner copy the owner of item with name "Bo" end end
            write_line the name of the owner of item
            write_line the label of copy item with label "rope" end
            """;

        Assert.Equal("Ann\nBo\nrope\n", Run.Program(source));
    }

    [Theory]
    [InlineData("define Point p Point 1.0 2.0\nwrite_line the x p", 3, 18, "Expected 'of' after the field name: the x of <record>, found 'p'.")]
    [InlineData("define Point p Point 1.0 2.0\nset p copy p x 1.0", 3, 14, "Expected 'with' after the record: copy <record> with <field> <value> end, found 'x'.")]
    [InlineData("define Point p Point 1.0 2.0\nset p copy p with 1.0 1.0", 3, 19, "Expected a field name after 'with', found '1.0'.")]
    [InlineData("define Point p Point 1.0 2.0\nwrite_line get_field x p", 3, 12, "'get_field' is no longer part of Pyrop; write the <field> of <record>.")]
    [InlineData("define Point p Point 1.0 2.0\nset p with_field x p 1.0", 3, 7, "'with_field' is no longer part of Pyrop; write copy <record> with <field> <value> end.")]
    [InlineData("define int the 1", 2, 12, "'the' is a reserved word and cannot be used as a variable name.")]
    [InlineData("define int of 1", 2, 12, "'of' is a reserved word and cannot be used as a variable name.")]
    [InlineData("define int copy 1", 2, 12, "'copy' is a reserved word and cannot be used as a variable name.")]
    [InlineData("define int with 1", 2, 12, "'with' is a reserved word and cannot be used as a variable name.")]
    [InlineData("write_line of", 2, 12, "Expected an expression, found keyword 'of'.")]
    [InlineData("copy p with x 1.0", 2, 1, "'copy' cannot start a statement.")]
    [InlineData("define Point p Point 1.0 2.0\nset p copy p with x 1.0 with x 2.0 end", 3, 30, "Field 'x' is changed twice in the copy started at line 3, column 7.")]
    [InlineData("define Point p Point 1.0 2.0\nset p copy p with x 2.0\nwrite_line p", 4, 1,
        "Expected 'with' or 'end' to close the copy started at line 3, column 7, found 'write_line'.")]
    [InlineData("define Point p Point 1.0 2.0\nwith x 1.0", 3, 1,
        "'with' cannot start a statement; it belongs to a copy, which changes several fields with one 'with' each: copy <record> with <field> <value> with <field> <value> end.")]
    public void FieldFormErrors(string body, int line, int column, string message)
    {
        var error = Run.Failure(Point + body);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal((line, column), (error.Position.Line, error.Position.Column));
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void OneCopyChangesSeveralFields()
    {
        Assert.Equal(
            "Point x 2.0 y 4.0\nPoint x 1.0 y 2.0\n",
            Run.Program(Point + "define Point p Point 1.0 2.0\ndefine Point q copy p with x 2.0 with y 4.0 end\nwrite_line q\nwrite_line p"));
    }

    [Fact]
    public void ANestedCopyClosesWithItsOwnEnd()
    {
        const string source = "record Line Point start Point stop end\ndefine Line l Line Point 0.0 0.0 Point 1.0 1.0\n" +
            "write_line copy l with stop copy the stop of l with x 5.0 with y 6.0 end with start Point 2.0 2.0 end";

        Assert.Equal("Line start (Point x 2.0 y 2.0) stop (Point x 5.0 y 6.0)\n", Run.Program(Point + source));
    }

    [Fact]
    public void AnUnfinishedFieldFormIsIncompleteInput()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run(Point + "define Point p Point 1.0 2.0");

        Assert.Equal(InputStatus.Incomplete, session.Probe("write_line the x"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("write_line the x of"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("set p copy p"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("set p copy p with x"));
    }
}
