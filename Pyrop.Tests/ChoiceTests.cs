namespace Pyrop.Tests;

public class ChoiceTests
{
    private const string Direction = "choice Direction north east south west end\n";

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
    public void VariantsAreValuesOfTheirChoice()
    {
        const string source = """
            define Direction facing north
            if equal facing west write_line "The sun sets ahead." else write_line facing
            set facing west
            if equal facing west write_line "The sun sets ahead."
            write_line equal north south
            choice Direction north east south west end
            """;

        Assert.Equal("north\nThe sun sets ahead.\nfalse\n", Run.Program(source));
    }

    [Fact]
    public void ChoicesWorkInFunctionsRecordsAndLoops()
    {
        const string source = """
            choice Direction north east south west end
            record Exit Direction way string target end
            function Direction opposite Direction d
            block
                if equal d north return south
                if equal d south return north
                if equal d east return west
                return east
            end
            define list Exit exits list_of Exit Exit north "hall" Exit east "garden" end
            for Exit door in exits block
                write the target of door
                write " is back to the "
                write_line opposite the way of door
            end
            write_line has list_of Direction north south end south
            """;

        Assert.Equal("hall is back to the south\ngarden is back to the west\ntrue\n", Run.Program(source));
    }

    [Theory]
    [InlineData("north", "north")]
    [InlineData("list_of Direction north east end", "[north, east]")]
    [InlineData("put put map_of Direction string end south \"cellar\" north \"hall\"", "{south: \"cellar\", north: \"hall\"}")]
    [InlineData("put map_of string Direction end \"way\" west", "{\"way\": west}")]
    [InlineData("Exit north \"hall\"", "Exit way north target \"hall\"")]
    [InlineData("Door Exit east \"yard\" true", "Door passage (Exit way east target \"yard\") open true")]
    [InlineData("convert string south", "south")]
    [InlineData("add convert string east \"!\"", "east!")]
    public void WritesTheTextFormOfVariants(string expression, string expected)
    {
        const string declarations = Direction + "record Exit Direction way string target end\nrecord Door Exit passage bool open end\n";

        Assert.Equal(expected + "\n", Run.Program(declarations + "write_line " + expression));
    }

    [Theory]
    [InlineData("convert Direction \"east\"", "east")]
    [InlineData("convert Direction \"  west\\t\"", "west")]
    [InlineData("equal convert Direction \"south\" south", "true")]
    [InlineData("convert Direction convert string north", "north")]
    public void ConvertsTextToAVariant(string expression, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program(Direction + "write_line " + expression));
    }

    [Theory]
    [InlineData("write_line convert Direction \"up\"", "Cannot convert \"up\" to Direction; its variants are north, east, south and west.")]
    [InlineData("write_line convert Direction \"North\"", "Cannot convert \"North\" to Direction; its variants are north, east, south and west.")]
    [InlineData("write_line convert Direction \"\"", "Cannot convert \"\" to Direction; its variants are north, east, south and west.")]
    [InlineData("write_line convert Direction \"north east\"", "Cannot convert \"north east\" to Direction; its variants are north, east, south and west.")]
    [InlineData("choice Only one end\nwrite_line convert Only \"two\"", "Cannot convert \"two\" to Only; its only variant is one.")]
    [InlineData("choice Pair yes no end\nwrite_line convert Pair \"maybe\"", "Cannot convert \"maybe\" to Pair; its variants are yes and no.")]
    public void ATextThatNamesNoVariantIsARuntimeError(string statement, string message)
    {
        var error = Run.Failure(Direction + statement);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal(12, error.Position.Column);
    }

    [Fact]
    public void ChoicesAreMapKeys()
    {
        const string source = """
            choice Direction north east south west end
            define map Direction int steps map_of Direction int end
            set steps put steps east 2
            set steps put steps north 1
            set steps put steps east 3
            write_line steps
            write_line get steps east
            write_line has steps west
            write_line keys_of remove steps east
            write_line equal steps put put map_of Direction int end north 1 east 3
            write_line get steps south
            """;

        var output = new StringOutput();
        var error = Assert.Single(Assert.Throws<PyropException>(() => new PyropSession(new StringReader(""), output).Run(source)).Errors);

        Assert.Equal("{east: 3, north: 1}\n3\nfalse\n[north]\ntrue\n", output.ToString());
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("The map has no key south.", error.Message);
    }

    [Fact]
    public void ChoicesCanBeUsedAboveTheirDeclaration()
    {
        const string source = """
            record Room string name Direction way end
            function Room hall return Room "hall" up
            write_line hall
            choice Direction up down end
            """;

        Assert.Equal("Room name \"hall\" way up\n", Run.Program(source));
    }

    [Fact]
    public void VariantsMayBeNamedInAnyStyle()
    {
        const string source = """
            choice Colour Red green DARK_blue end
            define list Colour all list_of Colour Red green DARK_blue end
            write_line all
            write_line convert Colour "DARK_blue"
            function void show Colour c write_line c
            show Red
            """;

        Assert.Equal("[Red, green, DARK_blue]\nDARK_blue\nRed\n", Run.Program(source));
    }

    [Theory]
    [InlineData("choice D end", 1, 10, "Choice 'D' needs at least one variant, such as choice D first end.")]
    [InlineData("choice D a a end", 1, 12, "Variant 'a' is declared twice in choice 'D'.")]
    [InlineData("choice D a add end", 1, 12, "'add' is a reserved word and cannot be used as a variant name.")]
    [InlineData("choice D a from end", 1, 12, "'from' is a reserved word and cannot be used as a variant name.")]
    [InlineData("choice D int a end", 1, 10, "'int' is a reserved word and cannot be used as a variant name.")]
    [InlineData("choice D a\nwrite_line a", 2, 1, "Expected a variant name or 'end' to close the choice started at line 1, column 1, found 'write_line'.")]
    [InlineData("choice D a else end", 1, 12, "Expected a variant name or 'end' to close the choice started at line 1, column 1, found 'else'.")]
    [InlineData("choice D a true end", 1, 12, "Expected a variant name or 'end' to close the choice started at line 1, column 1, found 'true'.")]
    [InlineData("choice D a 5 end", 1, 12, "Expected a variant name or 'end' to close the choice started at line 1, column 1, found '5'.")]
    [InlineData("choice D a \"b\" end", 1, 12, "Expected a variant name or 'end' to close the choice started at line 1, column 1, found string \"b\".")]
    [InlineData("choice D a end\nchoice D b end", 2, 8, "Choice 'D' is already declared.")]
    [InlineData("record D int x end\nchoice D a end", 2, 8, "'D' is already declared as a record.")]
    [InlineData("choice D a end\nrecord D int x end", 2, 8, "'D' is already declared as a choice.")]
    [InlineData("choice D a b end\nchoice E c b end", 2, 12, "Variant 'b' already belongs to the choice 'D'.")]
    [InlineData("block choice D a end end", 1, 7, "Choices can only be declared at the top level.")]
    [InlineData("function void f choice D a end", 1, 17, "Choices can only be declared at the top level.")]
    [InlineData("if true choice D a end", 1, 9, "Choices can only be declared at the top level.")]
    [InlineData("export define int x 1", 1, 8, "'export' must be followed by a function, record or choice definition, found 'define'.")]
    public void RejectsInvalidChoiceDeclarations(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(source, ErrorStage.Parse, line, column).Message);
    }

    [Theory]
    [InlineData("function int a return 1\nchoice D a end", 2, 10, "'a' is a function of this file and cannot also be a variant.")]
    [InlineData("choice D a end\nfunction int a return 1", 1, 10, "'a' is a function of this file and cannot also be a variant.")]
    [InlineData("record R int x end\nchoice D R end", 2, 10, "'R' is a record of this file and cannot also be a variant.")]
    [InlineData("choice D D end", 1, 10, "'D' is a choice of this file and cannot also be a variant.")]
    [InlineData("choice D a end\nchoice E D end", 2, 10, "'D' is a choice of this file and cannot also be a variant.")]
    [InlineData("function int D return 1\nchoice D a end", 2, 8, "'D' is a function of this file and cannot also name a choice.")]
    [InlineData("choice D a end\ndefine int a 1", 2, 12, "'a' is a variant and cannot be used as a variable name.")]
    [InlineData("choice D a end\ndefine int D 1", 2, 12, "'D' is a choice and cannot be used as a variable name.")]
    [InlineData("choice D a end\nset a 1", 2, 5, "'a' is a variant and cannot be used as a variable name.")]
    [InlineData("choice D a end\nfunction void f int a write_line a", 2, 21, "'a' is a variant and cannot be used as a parameter name.")]
    [InlineData("choice D a end\nfor int a in list_of int end write_line 1", 2, 9, "'a' is a variant and cannot be used as a loop variable name.")]
    [InlineData("choice D a end\ndefine int x 1\nfunction void f int y block\n    define int a 1\nend", 4, 16, "'a' is a variant and cannot be used as a variable name.")]
    public void VariantsAndChoicesClashWithOtherNames(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(source, ErrorStage.Parse, line, column).Message);
    }

    [Theory]
    [InlineData("define D d D", 2, 12, "'D' is a choice, not a value; name one of its variants, such as a.")]
    [InlineData("write_line D", 2, 12, "'D' is a choice, not a value; name one of its variants, such as a.")]
    [InlineData("D", 2, 1, "A statement cannot start with the choice 'D': a choice is a type, and no statement may start with a type name. Use one of its variants as a value, as in define D <name> a.")]
    [InlineData("a", 2, 1, "'a' cannot stand alone as a statement. This usually means the expression before it was given more arguments than its operation takes.")]
    [InlineData("write_line a b", 2, 14, "'b' cannot stand alone as a statement. This usually means the expression before it was given more arguments than its operation takes.")]
    [InlineData("define a x a", 2, 8, "'a' is a variant of the choice 'D', not a type.")]
    [InlineData("define map Q int m 1", 2, 12, "'Q' is not a type; no record or choice named 'Q' is declared or imported in this file.")]
    [InlineData("define map float D m 1", 2, 12, "A map key must be int, string, bool or a choice, found float.")]
    public void RejectsChoicesAndVariantsInTheWrongPlace(string statement, int line, int column, string message)
    {
        Assert.Equal(message, Failure("choice D a b end\n" + statement, ErrorStage.Parse, line, column).Message);
    }

    [Theory]
    [InlineData("define int x north", 2, 14, "Cannot initialize int variable 'x' with a value of type Direction.")]
    [InlineData("choice Item sword end\ndefine int i sword", 3, 14, "Cannot initialize int variable 'i' with a value of type Item.")]
    [InlineData("define Direction d north\nset d \"east\"", 3, 7, "Cannot set Direction variable 'd' to a value of type string.")]
    [InlineData("choice Turn left right end\nwrite_line equal north left", 3, 12, "'equal' cannot take Direction Turn. It accepts: T T (T stands for any type, the same wherever it appears).")]
    [InlineData("write_line greater north south", 2, 12, "'greater' cannot take Direction Direction. It accepts: int int, float float, string string.")]
    [InlineData("define int n convert int north", 2, 14, "Cannot convert Direction to int.")]
    [InlineData("define Direction d convert Direction 1", 2, 20, "Cannot convert int to Direction.")]
    [InlineData("choice Turn left right end\ndefine Turn t convert Turn north", 3, 15, "Cannot convert Direction to Turn.")]
    [InlineData("define bool b convert bool north", 2, 15, "Cannot convert Direction to bool.")]
    [InlineData("define map Direction int m put map_of Direction int end \"north\" 1", 2, 28, "'put' cannot take map Direction int string int. It accepts: list T int T, map K V K V (T, K and V stand for any types, each the same wherever it appears).")]
    public void ChecksChoiceTypes(string source, int line, int column, string message)
    {
        Assert.Equal(message, Failure(Direction + source, ErrorStage.Type, line, column).Message);
    }

    [Theory]
    [InlineData("define int choice 1", 1, 12)]
    [InlineData("write_line choice", 1, 12)]
    [InlineData("define string choice \"x\"", 1, 15)]
    [InlineData("set choice 1", 1, 5)]
    [InlineData("for int choice in list_of int end write_line 1", 1, 9)]
    [InlineData("define Direction choice north\nwrite_line choice", 1, 18)]
    [InlineData("function void greet string n write_line n\ndefine string name \"a\"\nwrite_line choice\ngreet name", 3, 12)]
    [InlineData("define string answer \"x\"\ndefine string choice answer", 2, 15)]
    [InlineData("define list Direction l list_of Direction end\nwrite_line convert Direction choice\nwrite_line l", 2, 30)]    [InlineData("choice add a end", 1, 1)]
    [InlineData("choice", 1, 1)]
    public void AChoiceKeywordThatCannotStartADeclarationIsReportedAsAReservedName(string source, int line, int column)
    {
        var program = Direction + source;
        var error = Failure(program, ErrorStage.Parse, line + 1, column);

        Assert.Equal("'choice' is a reserved word and cannot be used as a name.", error.Message);
        Assert.Equal(InputStatus.Ready, new PyropSession(new StringReader(""), new StringOutput()).Probe(program));
    }

    [Fact]
    public void PrintsChoicesInTheTree()
    {
        const string source = """
            export choice Direction north east end
            choice Mood calm end
            define map Direction Mood moods put map_of Direction Mood end north calm
            function Direction turn Direction d return east
            write_line turn north
            """;

        Assert.Equal(
            "export choice Direction north east end\n" +
            "choice Mood calm end\n" +
            "define map Direction Mood moods (put (map_of Direction Mood end) north calm)\n" +
            "function Direction turn Direction d\n    return east\n" +
            "write_line (turn north)\n",
            Tree(source));
    }

    [Fact]
    public void ChoiceNamesMayStartWithAnyLetter()
    {
        const string source = """
            choice směr sever jih end
            choice _mood calm end
            define map směr _mood moods put map_of směr _mood end jih calm
            function směr flip směr s if equal s sever return jih else return sever
            write_line flip sever
            write_line moods
            define list směr all list_of směr sever jih end
            for směr s in all write_line convert string s
            """;

        Assert.Equal("jih\n{jih: calm}\nsever\njih\n", Run.Program(source));
    }

    [Fact]
    public void AReservedWordAfterALowercaseTypeIsStillAName()
    {
        var program = "choice mood calm end\nrecord spot int x end\n";

        Assert.Equal("'choice' is a reserved word and cannot be used as a name.", Run.Failure(program + "define mood choice calm").Message);
        Assert.Equal("'record' is a reserved word and cannot be used as a name.", Run.Failure(program + "define spot record spot 1").Message);
        Assert.Equal("'record' is a reserved word and cannot be used as a name.", Run.Failure(program + "function void f spot record write_line 1").Message);
        Assert.Equal("calm\n", Run.Program(program + "define mood m calm\nrecord other mood m end\nwrite_line m"));
        Assert.Equal("'record' is a reserved word and cannot be used as a name.", Run.Failure(program + "record other mood record end").Message);
        Assert.Equal("'record' is a reserved word and cannot be used as a name.", Run.Failure(program + "function void f int a list record write_line a").Message);
    }

    [Theory]
    [InlineData("record item string label end\nwrite_line item \"lamp\"", "item label \"lamp\"")]
    [InlineData("choice q a end\nwrite_line a", "a")]
    [InlineData("function void f int a write_line a\nf 3", "3")]
    [InlineData("record holder spot spot end\nwrite_line holder spot 1", "holder spot (spot x 1)")]
    [InlineData("function mood g mood m return m\nwrite_line g calm", "calm")]
    public void AFieldOrParameterNamedLikeATypeDoesNotHideTheNextDeclaration(string next, string expected)
    {
        var program = "choice mood calm end\nrecord spot int x end\nrecord player string name mood mood end\n" +
            next + "\nwrite_line player \"Ann\" calm";

        Assert.Equal(expected + "\nplayer name \"Ann\" mood calm\n", Run.Program(program));
    }

    [Theory]
    [InlineData("record hero strng name end", "'strng' is not a type; no record or choice named 'strng' is declared or imported in this file.")]
    [InlineData("choice dir north end\nrecord hero dirr d end", "'dirr' is not a type; no record or choice named 'dirr' is declared or imported in this file.")]
    public void AMisspeltFirstFieldTypeIsReportedAsSuch(string source, string message)
    {
        Assert.Equal(message, Run.Failure(source).Message);
    }

    [Fact]
    public void AnUnfinishedChoiceIsIncompleteInput()
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());

        Assert.Equal(InputStatus.Incomplete, session.Probe("choice Direction"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("choice Direction north"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("export choice Direction north east"));
        Assert.Equal(InputStatus.Ready, session.Probe("choice Direction north end"));
        Assert.Equal(InputStatus.Ready, session.Probe("choice"));
        Assert.Equal(InputStatus.Ready, session.Probe("choice Direction north write_line 1"));
        Assert.Equal(InputStatus.AwaitingElse, session.Probe("choice Direction north end\nif equal north north write_line 1"));
        Assert.Equal(InputStatus.Incomplete, session.Probe("choice Direction north end\nwrite_line equal north"));
    }

    [Fact]
    public void ChoicesPersistAcrossReplInputs()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);

        session.Run("choice Direction north south end");
        session.Run("define Direction d north");
        session.Run("function Direction flip Direction x if equal x north return south else return north");
        session.Run("choice Direction north south end");
        session.Run("define map Direction int m put map_of Direction int end d 1");
        session.Run("write_line flip d");
        session.Run("write_line equal d convert Direction \"north\"");
        session.Run("write_line m");

        Assert.Equal("south\ntrue\n{north: 1}\n", output.ToString());
    }

    [Theory]
    [InlineData("choice Direction north end", "Choice 'Direction' was declared earlier in this session as choice Direction north south end, and cannot be declared again with other variants.")]
    [InlineData("choice Direction south north end", "Choice 'Direction' was declared earlier in this session as choice Direction north south end, and cannot be declared again with other variants.")]
    [InlineData("choice Direction north south up end", "Choice 'Direction' was declared earlier in this session as choice Direction north south end, and cannot be declared again with other variants.")]
    public void AChoiceCannotBeRedeclaredWithOtherVariantsInTheRepl(string redeclaration, string message)
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run("choice Direction north south end");

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(redeclaration)).Errors);

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal((1, 8), (error.Position.Line, error.Position.Column));
    }

    [Fact]
    public void AFailedReplInputDeclaresNoChoice()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);
        Assert.Throws<PyropException>(() => session.Run("choice Direction north end\nwrite_line add 1 true"));

        session.Run("choice Direction up down end");
        session.Run("define int north 1");
        session.Run("write_line list_of Direction up down end");

        Assert.Equal("[up, down]\n", output.ToString());
    }

    [Theory]
    [InlineData("define int north 1", "choice Direction north end", "'north' is already a global variable and cannot be used as a variant.")]
    [InlineData("define int Direction 1", "choice Direction north end", "'Direction' is already a global variable and cannot be used as a choice name.")]
    [InlineData("choice Direction north end", "define int north 1", "'north' is a variant and cannot be used as a variable name.")]
    [InlineData("choice Direction north end", "function int north return 1", "'north' is a variant in this file and cannot also be a function name.")]
    [InlineData("choice Direction north end", "function int Direction return 1", "'Direction' is a choice in this file and cannot also be a function name.")]
    [InlineData("function int north return 1", "choice Direction north end", "'north' is a function of this file and cannot also be a variant.")]
    [InlineData("choice Direction north end", "choice Turn north end", "Variant 'north' already belongs to the choice 'Direction'.")]
    [InlineData("choice Direction north end", "record Direction int x end", "'Direction' is already declared as a choice.")]
    [InlineData("record Direction int x end", "choice Direction north end", "'Direction' is already declared as a record.")]
    [InlineData("choice Direction north end", "choice Turn Direction end", "'Direction' is a choice of this file and cannot also be a variant.")]
    [InlineData("choice Direction North end", "record North int x end", "'North' is a variant in this file and cannot also name a record.")]
    public void ReplChoicesClashWithEarlierNames(string first, string second, string message)
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        session.Run(first);

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(second)).Errors);

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ADeclarationMayFollowAValueThatLooksLikeAType()
    {
        const string source = """
            define list int l list_of int end
            choice Colour Red green end
            define Colour c Red
            record Paint Colour colour end
            define map string Colour m map_of string Colour end
            function void show Colour shown write_line shown
            show c
            """;

        Assert.Equal("Red\n", Run.Program(source));
    }

    [Fact]
    public void AFieldMayBeNamedLikeAVariant()
    {
        const string source = Direction + """
            record Exit Direction north string target end
            write_line the north of Exit east "yard"
            """;

        Assert.Equal("east\n", Run.Program(source));
    }

    [Fact]
    public void AChoiceMayHaveManyVariantsSpreadOverLines()
    {
        var variants = string.Join("\n", Enumerable.Range(1, 5000).Select(index => $"v{index}"));
        var source = $"choice Big\n{variants}\nend\nwrite_line convert Big \"v4999\"\nwrite_line equal v1 convert Big \"v1\"";

        Assert.Equal("v4999\ntrue\n", Run.Program(source));
    }

    [Fact]
    public void ChoiceStatementsDoNotRun()
    {
        Assert.Equal("1\n", Run.Program("write_line 1\nchoice Direction north end"));
    }

    [Fact]
    public void ARecordWithAChoiceFieldComparesStructurally()
    {
        const string source = Direction + """
            record Exit Direction way string target end
            write_line equal Exit north "hall" Exit north "hall"
            write_line equal Exit north "hall" Exit south "hall"
            write_line copy Exit north "hall" with way west end
            """;

        Assert.Equal("true\nfalse\nExit way west target \"hall\"\n", Run.Program(source));
    }
}
