using System.Diagnostics;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public class CollectionTests
{
    private static string Evaluate(string expression) => Run.Program($"write_line {expression}").TrimEnd('\n');

    private static PyropError Failure(string source, ErrorStage stage)
    {
        var error = Run.Failure(source);
        Assert.Equal(stage, error.Stage);
        return error;
    }

    [Theory]
    [InlineData("list_of int 1 2 3 end", "[1, 2, 3]")]
    [InlineData("list_of int end", "[]")]
    [InlineData("list_of string end", "[]")]
    [InlineData("map_of string int end", "{}")]
    [InlineData("list_of float 1.0 0.5 -2.0 end", "[1.0, 0.5, -2.0]")]
    [InlineData("list_of bool true false end", "[true, false]")]
    [InlineData("list_of string \"lamp\" \"key\" end", "[\"lamp\", \"key\"]")]
    [InlineData("list_of list int list_of int 1 2 end list_of int end end", "[[1, 2], []]")]
    [InlineData("put put map_of string int end \"lamp\" 5 \"rope\" 2", "{\"lamp\": 5, \"rope\": 2}")]
    [InlineData("put map_of int string end 3 \"three\"", "{3: \"three\"}")]
    [InlineData("put map_of bool list int end true list_of int 1 end", "{true: [1]}")]
    [InlineData("put map_of string list int end \"a\" list_of int end", "{\"a\": []}")]
    [InlineData("list_of map string int map_of string int end end", "[{}]")]
    public void WritesTheTextFormOfCollections(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Fact]
    public void QuotesStringsInsideCollectionsWithSourceEscapes()
    {
        Assert.Equal(
            "[\"a, b\", \"say \\\"hi\\\"\", \"back\\\\slash\", \"line\\nnext\", \"tab\\tstop\", \"\"]\n",
            Run.Program("write_line list_of string \"a, b\" \"say \\\"hi\\\"\" \"back\\\\slash\" \"line\\nnext\" \"tab\\tstop\" \"\" end"));
    }

    [Fact]
    public void QuotesMapKeysAndValues()
    {
        Assert.Equal("{\"k\\\"ey\": \"va\\nlue\"}", Evaluate("put map_of string string end \"k\\\"ey\" \"va\\nlue\""));
    }

    [Fact]
    public void ConvertsCollectionsToTheirTextForm()
    {
        Assert.Equal("[[1, 2], []]", Evaluate("convert string list_of list int list_of int 1 2 end list_of int end end"));
        Assert.Equal("2", Evaluate("length_of convert string list_of int end"));
        Assert.Equal("[\"x\"]", Evaluate("convert list string list_of string \"x\" end"));
    }

    [Fact]
    public void WriteLeavesAStringOutsideACollectionUnquoted()
    {
        Assert.Equal("say \"hi\"[\"say \\\"hi\\\"\"]", Run.Program("write \"say \\\"hi\\\"\" write list_of string \"say \\\"hi\\\"\" end"));
    }

    [Theory]
    [InlineData("convert int list_of int 1 end", "Cannot convert list int to int.")]
    [InlineData("convert list string \"[]\"", "Cannot convert string to list string.")]
    [InlineData("convert list float list_of int 1 end", "Cannot convert list int to list float.")]
    public void ConvertsCollectionsOnlyToString(string expression, string message)
    {
        Assert.Equal(message, Failure($"write_line {expression}", ErrorStage.Type).Message);
    }

    [Theory]
    [InlineData("get list_of string \"a\" \"b\" end 1", "b")]
    [InlineData("push list_of int 1 end 2", "[1, 2]")]
    [InlineData("push list_of int end 7", "[7]")]
    [InlineData("put list_of int 1 2 3 end 1 9", "[1, 9, 3]")]
    [InlineData("remove list_of int 1 2 3 end 0", "[2, 3]")]
    [InlineData("remove list_of int 1 2 3 end 2", "[1, 2]")]
    [InlineData("has list_of string \"a\" \"b\" end \"b\"", "true")]
    [InlineData("has list_of string \"a\" \"b\" end \"c\"", "false")]
    [InlineData("has list_of int end 1", "false")]
    [InlineData("has list_of list int list_of int 1 2 end end list_of int 1 2 end", "true")]
    [InlineData("length_of list_of int 1 2 3 end", "3")]
    [InlineData("length_of list_of int end", "0")]
    [InlineData("length_of put put map_of string int end \"a\" 1 \"a\" 2", "1")]
    [InlineData("length_of \"čau\"", "3")]
    [InlineData("get get list_of list int list_of int 1 2 end end 0 1", "2")]
    public void OperatesOnLists(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("get put map_of string int end \"lamp\" 5 \"lamp\"", "5")]
    [InlineData("has put map_of string int end \"lamp\" 5 \"lamp\"", "true")]
    [InlineData("has put map_of string int end \"lamp\" 5 \"rope\"", "false")]
    [InlineData("remove put put map_of int int end 1 10 2 20 1", "{2: 20}")]
    [InlineData("keys_of put put map_of string int end \"b\" 1 \"a\" 2", "[\"b\", \"a\"]")]
    [InlineData("keys_of map_of bool int end", "[]")]
    [InlineData("get put map_of string list int end \"a\" list_of int 1 end \"a\"", "[1]")]
    public void OperatesOnMaps(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Fact]
    public void MapsKeepInsertionOrder()
    {
        const string source =
            "def map string int m map_of string int end\n" +
            "set m put m \"a\" 1\nset m put m \"b\" 2\nset m put m \"c\" 3\n" +
            "set m put m \"a\" 10\nwrite_line m\n" +
            "set m remove m \"b\"\nset m put m \"b\" 20\nwrite_line m\nwrite_line keys_of m\n" +
            "set m remove m \"a\"\nwrite_line m";

        Assert.Equal(
            "{\"a\": 10, \"b\": 2, \"c\": 3}\n{\"a\": 10, \"c\": 3, \"b\": 20}\n[\"a\", \"c\", \"b\"]\n{\"c\": 3, \"b\": 20}\n",
            Run.Program(source));
    }

    [Fact]
    public void ChangingACopyLeavesTheOriginalUnchanged()
    {
        const string source =
            "def list int a list_of int 1 2 end\ndef list int b a\nset b push b 3\nset b put b 0 9\n" +
            "write_line a\nwrite_line b\n" +
            "def map string int m put map_of string int end \"x\" 1\ndef map string int n m\n" +
            "set n put n \"x\" 2\nset n put n \"y\" 3\nwrite_line m\nwrite_line n";

        Assert.Equal("[1, 2]\n[9, 2, 3]\n{\"x\": 1}\n{\"x\": 2, \"y\": 3}\n", Run.Program(source));
    }

    [Fact]
    public void FunctionsCannotChangeTheCallersCollections()
    {
        const string source =
            "function list int grow list int items block\n" +
            "    set items push items 99\n    return items\nend\n" +
            "function void change map string list int m block\n" +
            "    set m put m \"k\" push get m \"k\" 5\n    write_line m\nend\n" +
            "def list int a list_of int 1 end\ndef list int b grow a\nwrite_line a\nwrite_line b\n" +
            "def map string list int m put map_of string list int end \"k\" list_of int 1 end\n" +
            "change m\nwrite_line m";

        Assert.Equal("[1]\n[1, 99]\n{\"k\": [1, 5]}\n{\"k\": [1]}\n", Run.Program(source));
    }

    [Fact]
    public void NestedCollectionsChangeOnlyThroughSet()
    {
        const string source =
            "def list list int grid list_of list int list_of int 0 0 end list_of int 0 0 end end\n" +
            "def list int row get grid 0\nset row put row 1 5\n" +
            "write_line grid\nset grid put grid 0 row\nwrite_line grid";

        Assert.Equal("[[0, 0], [0, 0]]\n[[0, 5], [0, 0]]\n", Run.Program(source));
    }

    [Theory]
    [InlineData("equal list_of int 1 2 end list_of int 1 2 end", "true")]
    [InlineData("equal list_of int 1 2 end list_of int 2 1 end", "false")]
    [InlineData("equal list_of int 1 end list_of int 1 1 end", "false")]
    [InlineData("equal list_of int end list_of int end", "true")]
    [InlineData("equal map_of string int end map_of string int end", "true")]
    [InlineData("equal list_of list int list_of int 1 end list_of int end end list_of list int list_of int 1 end list_of int end end", "true")]
    [InlineData("equal list_of list int list_of int 1 end end list_of list int list_of int 2 end end", "false")]
    [InlineData("equal put put map_of int int end 1 1 2 2 put put map_of int int end 2 2 1 1", "true")]
    [InlineData("equal put map_of int int end 1 1 put map_of int int end 1 2", "false")]
    [InlineData("equal put map_of int int end 1 1 put map_of int int end 2 1", "false")]
    [InlineData("equal remove put map_of int int end 1 1 1 map_of int int end", "true")]
    [InlineData("equal list_of float 0.0 end list_of float multiply -1.0 0.0 end", "true")]
    [InlineData("equal put map_of string list int end \"a\" list_of int 1 end put map_of string list int end \"a\" list_of int 1 end", "true")]
    [InlineData("equal 1 1", "true")]
    [InlineData("equal \"a\" \"b\"", "false")]
    public void ComparesCollectionsStructurally(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("equal list_of int 1 end list_of float 1.0 end", "'equal' cannot take list int list float. It accepts: T T (T stands for any type, the same wherever it appears).")]
    [InlineData("equal 1 \"1\"", "'equal' cannot take int string. It accepts: T T (T stands for any type, the same wherever it appears).")]
    [InlineData("push list_of int 1 end \"a\"", "'push' cannot take list int string. It accepts: list T T (T stands for any type, the same wherever it appears).")]
    [InlineData("get list_of int 1 end \"0\"", "'get' cannot take list int string. It accepts: list T int, map K V K, string int (T, K and V stand for any types, each the same wherever it appears).")]
    [InlineData("get put map_of string int end \"a\" 1 1", "'get' cannot take map string int int. It accepts: list T int, map K V K, string int (T, K and V stand for any types, each the same wherever it appears).")]
    [InlineData("length_of 5", "'length_of' cannot take int. It accepts: string, list T, map K V (T, K and V stand for any types, each the same wherever it appears).")]
    [InlineData("keys_of list_of int 1 end", "'keys_of' cannot take list int. It accepts: map K V (K and V stand for any types, each the same wherever it appears).")]
    public void RejectsOperandsOfTheWrongType(string expression, string message)
    {
        Assert.Equal(message, Failure($"write_line {expression}", ErrorStage.Type).Message);
    }

    [Fact]
    public void ResultTypesFollowTheElementType()
    {
        Assert.Equal("4", Evaluate("add get list_of int 1 2 end 1 2"));
        Assert.Equal("lamp!", Evaluate("add get put map_of int string end 1 \"lamp\" 1 \"!\""));
        Assert.Equal("3", Evaluate("add get keys_of put map_of int bool end 2 true 0 1"));

        var error = Failure("def string s get list_of int 1 end 0", ErrorStage.Type);
        Assert.Equal("Cannot initialize string variable 's' with a value of type int.", error.Message);
    }

    [Theory]
    [InlineData("get list_of int 1 2 3 end 3", "List index 3 is out of range for a list of 3 items, numbered from 0.")]
    [InlineData("get list_of int 1 end -1", "List index -1 is out of range for a list of 1 item, numbered from 0.")]
    [InlineData("get list_of int end 0", "List index 0 is out of range for an empty list.")]
    [InlineData("put list_of int 1 2 end 2 0", "List index 2 is out of range for a list of 2 items, numbered from 0.")]
    [InlineData("remove list_of string end 0", "List index 0 is out of range for an empty list.")]
    [InlineData("get list_of int 1 end 9223372036854775807", "List index 9223372036854775807 is out of range for a list of 1 item, numbered from 0.")]
    [InlineData("get put map_of string int end \"lamp\" 5 \"rope\"", "The map has no key \"rope\".")]
    [InlineData("get map_of int int end 7", "The map has no key 7.")]
    [InlineData("get map_of bool int end false", "The map has no key false.")]
    [InlineData("remove map_of string int end \"a\\\"b\"", "The map has no key \"a\\\"b\".")]
    public void ReportsMissingItemsAtRuntime(string expression, string message)
    {
        var error = Failure($"write_line {expression}", ErrorStage.Runtime);
        Assert.Equal(message, error.Message);
        Assert.Equal(12, error.Position.Column);
    }

    [Theory]
    [InlineData("def map float int m map_of float int end", 9, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def map list int int m map_of int int end", 9, "A map key must be int, string, bool or a choice, found list int.")]
    [InlineData("def int m length_of map_of map int int string end", 28, "A map key must be int, string, bool or a choice, found map int int.")]
    [InlineData("function int f map float int m return 0", 20, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def int x 0 write_line convert map float int x", 36, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def int x length_of list_of map float int end", 33, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def list int x empty list int", 16, "'empty' is no longer part of Pyrop; write an empty list as list_of int end and an empty map as map_of string int end.")]
    [InlineData("def map string int m map_of string void end", 36, "A map value cannot have type void.")]
    [InlineData("def list void x list_of int end", 10, "A type argument cannot have type void.")]
    [InlineData("def list int x list_of void end", 24, "A list item cannot have type void.")]
    [InlineData("def map string int m map_of string", 35, "Expected a type, found end of input.")]
    public void RejectsInvalidCollectionTypesWhileParsing(string source, int column, string message)
    {
        var error = Failure(source, ErrorStage.Parse);
        Assert.Equal(message, error.Message);
        Assert.Equal(column, error.Position.Column);
    }

    [Fact]
    public void ChecksEveryItemOfAListOf()
    {
        var errors = Run.Failures("def list int a list_of int 1 \"two\" 3 4.0 end");

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(ErrorStage.Type, error.Stage));
        Assert.Equal("Every item of 'list_of int' must be int, found string.", errors[0].Message);
        Assert.Equal(30, errors[0].Position.Column);
        Assert.Equal("Every item of 'list_of int' must be int, found float.", errors[1].Message);
    }

    [Fact]
    public void MapOfBuildsAMapInTheOrderWritten()
    {
        const string source = """
            def map string int prices map_of string int
                "lamp" 5
                "rope" add 1 1
            end
            write_line prices
            write_line map_of string int end
            write_line map_of int list int 1 list_of int 1 end 2 list_of int end end
            write_line map_of string int "a" 1 "b" 2 "a" 3 end
            """;

        Assert.Equal("{\"lamp\": 5, \"rope\": 2}\n{}\n{1: [1], 2: []}\n{\"a\": 3, \"b\": 2}\n", Run.Program(source));
    }

    [Fact]
    public void ChecksEveryKeyAndValueOfAMapOf()
    {
        var errors = Run.Failures("def map string int m map_of string int \"a\" 1 2 3 \"c\" \"d\" end");

        Assert.Equal(
            ["Every key of 'map_of string int' must be string, found int.", "Every value of 'map_of string int' must be int, found string."],
            errors.Select(error => error.Message));
        Assert.Equal(46, errors[0].Position.Column);
        Assert.Equal(54, errors[1].Position.Column);
    }

    [Theory]
    [InlineData("def map string int m map_of string int \"a\" end", 44, "The last key of the map_of started at line 1, column 22 has no value; every key is followed by its value.")]
    [InlineData("def map string int m map_of string int \"a\" 1\nwrite_line m", 1, "Expected a key or 'end' to close the map_of started at line 1, column 22, found 'write_line'.")]
    [InlineData("def map string int m map_of string int \"a\" 1 \"b\"\nwrite_line m", 1, "Expected the value of the last key of the map_of started at line 1, column 22, found 'write_line'.")]
    [InlineData("def int n length_of map_of list int int end", 28, "A map key must be int, string, bool or a choice, found list int.")]
    public void RejectsMalformedMapOf(string source, int column, string message)
    {
        var error = Failure(source, ErrorStage.Parse);
        Assert.Equal(message, error.Message);
        Assert.Equal(column, error.Position.Column);
    }

    [Fact]
    public void ChecksTheDeclaredTypeOfACollection()
    {
        var error = Failure("def list float a list_of int 1 end", ErrorStage.Type);
        Assert.Equal("Cannot initialize list float variable 'a' with a value of type list int.", error.Message);
    }

    [Theory]
    [InlineData("def list int a list_of int 1 2 write_line a", "Expected a list item or 'end' to close the list_of started at")]
    [InlineData("write_line list_of int end end", "'end' without a matching 'block'.")]
    [InlineData("list_of int 1 end", "'list_of' cannot start a statement.")]
    [InlineData("map_of string int end", "'map_of' cannot start a statement.")]
    [InlineData("write_line empty list int", "'empty' is no longer part of Pyrop;")]
    [InlineData("def int list 0", "'list' is a reserved word and cannot be used as a variable name.")]
    [InlineData("def int push 0", "'push' is a reserved word and cannot be used as a variable name.")]
    [InlineData("def int empty 0", "'empty' is a reserved word and cannot be used as a variable name.")]
    public void ReportsMisplacedCollectionWords(string source, string message)
    {
        Assert.StartsWith(message, Failure(source, ErrorStage.Parse).Message);
    }

    [Theory]
    [InlineData("write_line item_at list_of int 1 end 0", "'item_at' is no longer part of Pyrop; use get <list> <index>.")]
    [InlineData("write_line value_of map_of int int end 1", "'value_of' is no longer part of Pyrop; use get <map> <key>.")]
    [InlineData("write_line char_at \"abc\" 1", "'char_at' is no longer part of Pyrop; use get <text> <position>.")]
    [InlineData("write_line contains list_of int end 1", "'contains' is no longer part of Pyrop; use has <list> <item>.")]
    [InlineData("write_line has_key map_of int int end 1", "'has_key' is no longer part of Pyrop; use has <map> <key>.")]
    [InlineData("write_line remove_at list_of int 1 end 0", "'remove_at' is no longer part of Pyrop; use remove <list> <index>.")]
    [InlineData("write_line remove_key map_of int int end 1", "'remove_key' is no longer part of Pyrop; use remove <map> <key>.")]
    [InlineData("write_line replace_at list_of int 1 end 0 2", "'replace_at' is no longer part of Pyrop; use put <list> <index> <item>.")]
    [InlineData("write_line negate 5", "'negate' is no longer part of Pyrop; write subtract 0 <number>, or subtract 0.0 <number> for a float.")]
    [InlineData("negate 5", "'negate' is no longer part of Pyrop; write subtract 0 <number>, or subtract 0.0 <number> for a float.")]
    [InlineData("write_line attempt value_of map_of int int end 1", "'value_of' is no longer part of Pyrop; use get <map> <key>.")]
    [InlineData("def int contains 1", "'contains' is a reserved word and cannot be used as a variable name.")]
    public void RetiredWordsSayWhatReplacedThem(string source, string message)
    {
        Assert.Equal(message, Failure(source, ErrorStage.Parse).Message);
    }

    [Fact]
    public void SuggestsTheUnderscoredSpelling()
    {
        Assert.Contains("did you mean 'list_of'?", Failure("def int n length_of listof", ErrorStage.Type).Message);
    }

    [Fact]
    public void ListOfNestsInsideBlocks()
    {
        const string source =
            "block\n    def list list int a list_of list int\n        list_of int 1 end\n        list_of int 2 3 end\n    end\n" +
            "    write_line a\nend";

        Assert.Equal("[[1], [2, 3]]\n", Run.Program(source));
    }

    [Theory]
    [InlineData("def list int a list_of int 1 2", InputStatus.Incomplete)]
    [InlineData("write_line list_of list int list_of int 1 end", InputStatus.Incomplete)]
    [InlineData("def list int a list_of", InputStatus.Incomplete)]
    [InlineData("def map string", InputStatus.Incomplete)]
    [InlineData("write_line map_of string", InputStatus.Incomplete)]
    [InlineData("write_line map_of string int \"a\" 1", InputStatus.Incomplete)]
    [InlineData("def list int a list_of int 1 2 end", InputStatus.Ready)]
    public void AnUnfinishedListOfAsksForMoreInput(string source, InputStatus expected)
    {
        var session = new PyropSession(new StringReader(""), new StringOutput());
        Assert.Equal(expected, session.Probe(source));
    }

    [Fact]
    public void CollectionsPersistAcrossReplInputs()
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output);
        session.Run("def list string bag list_of string end");
        session.Run("set bag push bag \"lamp\"");
        Assert.Throws<PyropException>(() => session.Run("def list int bag list_of int end"));
        session.Run("write_line bag");

        Assert.Equal("[\"lamp\"]\n", output.ToString());
    }

    [Fact]
    public void TheSizeLimitIsTenMillionItems()
    {
        Assert.Equal(10_000_000, CollectionOperations.MaxCount);
        Assert.Equal(CollectionOperations.MaxCount, new PyropSession(new StringReader(""), new StringOutput()).CollectionLimit);
    }

    [Theory]
    [InlineData(
        "def list int a list_of int 1 2 end\nset a push a 3\nwrite_line a\nset a push a 4",
        4, 7, "A list cannot hold more than 3 items.", "[1, 2, 3]\n")]
    [InlineData(
        "def list int a list_of int 1 2 3 end\nwrite_line a\nwrite_line list_of int 1 2 3 4 end",
        3, 12, "A list cannot hold more than 3 items.", "[1, 2, 3]\n")]
    [InlineData(
        "def map int int m put put put map_of int int end 1 1 2 2 3 3\nset m put m 3 30\nwrite_line m\nset m put m 4 4",
        4, 7, "A map cannot hold more than 3 items.", "{1: 1, 2: 2, 3: 30}\n")]
    public void RefusesToGrowBeyondTheSizeLimit(string source, int line, int column, string message, string written)
    {
        var output = new StringOutput();
        var session = new PyropSession(new StringReader(""), output) { CollectionLimit = 3 };

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(source)).Errors);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal(line, error.Position.Line);
        Assert.Equal(column, error.Position.Column);
        Assert.Equal(written, output.ToString());
    }

    [Fact]
    public void QuotingIsCheckedAgainstTheTextLimit()
    {
        const string source = "def string s repeat \"\\\\\" 50000001\nwrite_line length_of convert string list_of string s end";

        var error = Failure(source, ErrorStage.Runtime);
        Assert.Equal("The text form of this value would exceed the largest allowed text size.", error.Message);
        Assert.Equal(2, error.Position.Line);
    }

    [Fact]
    public void ReplacingANestedValueInAMapIsFast()
    {
        const string source =
            "def list int big list_of int end\ndef int i 0\n" +
            "while less i 200000 block\n    set big push big i\n    set i add i 1\nend\n" +
            "def map int list int m put map_of int list int end 1 big\n" +
            "set i 0\nwhile less i 2000 block\n    set m put m 1 put get m 1 199999 i\n    set i add i 1\nend\n" +
            "write_line get get m 1 199999";

        var watch = Stopwatch.StartNew();
        var output = Run.Program(source);
        watch.Stop();

        Assert.Equal("1999\n", output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"Took {watch.Elapsed}.");
    }

    [Fact]
    public void RefusesATextFormThatWouldBeTooLong()
    {
        const string source =
            "def string s repeat \"x\" 30000000\ndef list string a list_of string s s s s end\nwrite_line a";

        var error = Failure(source, ErrorStage.Runtime);
        Assert.Equal("The text form of this value would exceed the largest allowed text size.", error.Message);
        Assert.Equal(3, error.Position.Line);
    }

    [Fact]
    public void PushingAHundredThousandItemsIsFast()
    {
        const string source =
            "def list int a list_of int end\ndef int i 0\n" +
            "while less i 100000 block\n    set a push a i\n    set i add i 1\nend\n" +
            "write_line length_of a\nwrite_line get a 99999";

        Run.Program(source);
        var watch = Stopwatch.StartNew();
        var output = Run.Program(source);
        watch.Stop();

        Assert.Equal("100000\n99999\n", output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    [Theory]
    [InlineData("slice list_of int 1 2 3 4 end 1 2", "[2, 3]")]
    [InlineData("slice list_of int 1 2 3 end 0 3", "[1, 2, 3]")]
    [InlineData("slice list_of int 1 2 3 end 3 0", "[]")]
    [InlineData("slice list_of int end 0 0", "[]")]
    [InlineData("slice list_of string \"a\" \"b\" end 1 1", "[\"b\"]")]
    [InlineData("positions_of list_of string \"a\" \"b\" \"c\" end", "[0, 1, 2]")]
    [InlineData("positions_of list_of int end", "[]")]
    public void SlicesAListAndGivesItsPositions(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("slice list_of int 1 2 3 end 4 0", "Position 4 is out of range for a list of 3 items, numbered from 0.")]
    [InlineData("slice list_of int 1 2 3 end -1 1", "Position -1 is out of range for a list of 3 items, numbered from 0.")]
    [InlineData("slice list_of int end 1 0", "Position 1 is out of range for an empty list, numbered from 0.")]
    [InlineData("slice list_of int 1 2 3 end 1 -1", "'slice' cannot take a negative count (-1).")]
    [InlineData("slice list_of int 1 2 3 end 2 5", "Cannot take 5 items from position 2 of a list of 3 items.")]
    [InlineData("slice list_of int 1 end 1 1", "Cannot take 1 item from position 1 of a list of 1 item.")]
    public void SliceFailsWhereSubstringWould(string expression, string message)
    {
        Assert.Equal(message, Failure($"write_line {expression}", ErrorStage.Runtime).Message);
    }

    [Fact]
    public void SliceLeavesTheListUnchanged()
    {
        Assert.Equal(
            "[1, 2, 3]\n[2]\n",
            Run.Program("def list int a list_of int 1 2 3 end\ndef list int b slice a 1 1\nwrite_line a\nwrite_line b"));
    }

    [Fact]
    public void ForWalksAListsPositions()
    {
        const string source =
            "def list string items list_of string \"lamp\" \"key\" end\n" +
            "for int index in positions_of items write_line fill \"{}: {}\" list_of string convert string index get items index end";

        Assert.Equal("0: lamp\n1: key\n", Run.Program(source));
    }

    [Theory]
    [InlineData("sort list_of int 3 -1 2 end", "[-1, 2, 3]")]
    [InlineData("sort list_of int 2 1 2 end", "[1, 2, 2]")]
    [InlineData("sort list_of float 2.5 -0.5 1.0 end", "[-0.5, 1.0, 2.5]")]
    [InlineData("sort list_of string \"pear\" \"Zoo\" \"apple\" \"éclair\" end", "[\"Zoo\", \"apple\", \"pear\", \"éclair\"]")]
    [InlineData("sort list_of int end", "[]")]
    [InlineData("sort keys_of put put map_of string int end \"b\" 1 \"a\" 2", "[\"a\", \"b\"]")]
    [InlineData("sort keys_of put put map_of int bool end 2 true 1 false", "[1, 2]")]
    public void SortsNumbersAndTexts(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("sort list_of bool true false end", "'sort' cannot take list bool. It accepts: list int, list float, list string.")]
    [InlineData("sort list_of list int end", "'sort' cannot take list list int. It accepts: list int, list float, list string.")]
    [InlineData("sort 5", "'sort' cannot take int. It accepts: list int, list float, list string.")]
    public void SortRejectsListsItCannotOrder(string expression, string message)
    {
        Assert.Equal(message, Failure($"write_line {expression}", ErrorStage.Type).Message);
    }

    [Fact]
    public void SortRejectsChoicesAndRecords()
    {
        Assert.Equal(ErrorStage.Type, Run.Failure("choice Level low high end\nwrite_line sort list_of Level high low end").Stage);
        Assert.Equal(ErrorStage.Type, Run.Failure("record Point int x\nwrite_line sort list_of Point end").Stage);
    }

    [Theory]
    [InlineData("reverse list_of int 1 2 3 end", "[3, 2, 1]")]
    [InlineData("reverse list_of string \"a\" end", "[\"a\"]")]
    [InlineData("reverse list_of list int list_of int 1 end list_of int end end", "[[], [1]]")]
    [InlineData("reverse list_of bool end", "[]")]
    [InlineData("reverse sort list_of int 2 3 1 end", "[3, 2, 1]")]
    public void ReversesAnyList(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("distinct list_of int 3 1 3 2 1 end", "[3, 1, 2]")]
    [InlineData("distinct list_of string \"a\" \"A\" \"a\" end", "[\"a\", \"A\"]")]
    [InlineData("distinct list_of float 0.5 0.5 1.5 end", "[0.5, 1.5]")]
    [InlineData("distinct list_of bool true true end", "[true]")]
    [InlineData("distinct list_of int end", "[]")]
    [InlineData("distinct list_of list int list_of int 1 end list_of int 2 end list_of int 1 end end", "[[1], [2]]")]
    [InlineData("distinct list_of option int some 1 none int some 1 none int end", "[some 1, none]")]
    public void DistinctKeepsTheFirstOfEachEqualItem(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Fact]
    public void DistinctTreatsMapsWithTheSameContentAsEqualWhateverTheirOrder()
    {
        Assert.Equal(
            "1",
            Evaluate("length_of distinct list_of map string int put put map_of string int end \"a\" 1 \"b\" 2 put put map_of string int end \"b\" 2 \"a\" 1 end"));
    }

    [Fact]
    public void DistinctComparesRecordsAndChoicesLikeEqual()
    {
        const string source = """
            record Point int x int y
            choice Move go int stay end
            write_line length_of distinct list_of Point Point 1 2 Point 1 2 Point 2 1 end
            write_line length_of distinct list_of Move go 1 go 1 go 2 stay stay end
            """;

        Assert.Equal("2\n3\n", Run.Program(source));
    }

    [Fact]
    public void DistinctFailsOnValuesNestedBeyondTheLimit()
    {
        const string source = "record Tree list Tree kids\n" +
            "def Tree a Tree list_of Tree Tree list_of Tree Tree list_of Tree end end end\n" +
            "write_line length_of distinct list_of Tree a a end";
        var session = new PyropSession(new StringReader(""), new StringOutput()) { NestingLimit = 4 };

        var error = Assert.Single(Assert.Throws<PyropException>(() => session.Run(source)).Errors);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal("'distinct' cannot compare values nested this deeply.", error.Message);
    }

    [Fact]
    public void TheNewListOperationsLeaveTheListUnchanged()
    {
        Assert.Equal(
            "[2, 1, 2]\n[1, 2, 2]\n[2, 1, 2]\n[2, 1]\n",
            Run.Program("def list int a list_of int 2 1 2 end\ndef list int s sort a\ndef list int r reverse a\ndef list int d distinct a\nwrite_line a\nwrite_line s\nwrite_line r\nwrite_line d"));
    }

    [Theory]
    [InlineData("slice")]
    [InlineData("positions_of")]
    [InlineData("sort")]
    [InlineData("reverse")]
    [InlineData("distinct")]
    public void TheListOperationsAreReservedWords(string word)
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure($"def int {word} 1").Stage);
    }
}
