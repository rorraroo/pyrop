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
    [InlineData("empty list string", "[]")]
    [InlineData("empty map string int", "{}")]
    [InlineData("list_of float 1.0 0.5 -2.0 end", "[1.0, 0.5, -2.0]")]
    [InlineData("list_of bool true false end", "[true, false]")]
    [InlineData("list_of string \"lamp\" \"key\" end", "[\"lamp\", \"key\"]")]
    [InlineData("list_of list int list_of int 1 2 end list_of int end end", "[[1, 2], []]")]
    [InlineData("put put empty map string int \"lamp\" 5 \"rope\" 2", "{\"lamp\": 5, \"rope\": 2}")]
    [InlineData("put empty map int string 3 \"three\"", "{3: \"three\"}")]
    [InlineData("put empty map bool list int true list_of int 1 end", "{true: [1]}")]
    [InlineData("put empty map string list int \"a\" empty list int", "{\"a\": []}")]
    [InlineData("list_of map string int empty map string int end", "[{}]")]
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
        Assert.Equal("{\"k\\\"ey\": \"va\\nlue\"}", Evaluate("put empty map string string \"k\\\"ey\" \"va\\nlue\""));
    }

    [Fact]
    public void ConvertsCollectionsToTheirTextForm()
    {
        Assert.Equal("[[1, 2], []]", Evaluate("convert string list_of list int list_of int 1 2 end empty list int end"));
        Assert.Equal("2", Evaluate("length_of convert string empty list int"));
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
    [InlineData("item_at list_of string \"a\" \"b\" end 1", "b")]
    [InlineData("push list_of int 1 end 2", "[1, 2]")]
    [InlineData("push empty list int 7", "[7]")]
    [InlineData("replace_at list_of int 1 2 3 end 1 9", "[1, 9, 3]")]
    [InlineData("remove_at list_of int 1 2 3 end 0", "[2, 3]")]
    [InlineData("remove_at list_of int 1 2 3 end 2", "[1, 2]")]
    [InlineData("contains list_of string \"a\" \"b\" end \"b\"", "true")]
    [InlineData("contains list_of string \"a\" \"b\" end \"c\"", "false")]
    [InlineData("contains empty list int 1", "false")]
    [InlineData("contains list_of list int list_of int 1 2 end end list_of int 1 2 end", "true")]
    [InlineData("length_of list_of int 1 2 3 end", "3")]
    [InlineData("length_of empty list int", "0")]
    [InlineData("length_of put put empty map string int \"a\" 1 \"a\" 2", "1")]
    [InlineData("length_of \"čau\"", "3")]
    [InlineData("item_at item_at list_of list int list_of int 1 2 end end 0 1", "2")]
    public void OperatesOnLists(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("value_of put empty map string int \"lamp\" 5 \"lamp\"", "5")]
    [InlineData("has_key put empty map string int \"lamp\" 5 \"lamp\"", "true")]
    [InlineData("has_key put empty map string int \"lamp\" 5 \"rope\"", "false")]
    [InlineData("remove_key put put empty map int int 1 10 2 20 1", "{2: 20}")]
    [InlineData("keys_of put put empty map string int \"b\" 1 \"a\" 2", "[\"b\", \"a\"]")]
    [InlineData("keys_of empty map bool int", "[]")]
    [InlineData("value_of put empty map string list int \"a\" list_of int 1 end \"a\"", "[1]")]
    public void OperatesOnMaps(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Fact]
    public void MapsKeepInsertionOrder()
    {
        const string source =
            "def map string int m empty map string int\n" +
            "set m put m \"a\" 1\nset m put m \"b\" 2\nset m put m \"c\" 3\n" +
            "set m put m \"a\" 10\nwrite_line m\n" +
            "set m remove_key m \"b\"\nset m put m \"b\" 20\nwrite_line m\nwrite_line keys_of m\n" +
            "set m remove_key m \"a\"\nwrite_line m";

        Assert.Equal(
            "{\"a\": 10, \"b\": 2, \"c\": 3}\n{\"a\": 10, \"c\": 3, \"b\": 20}\n[\"a\", \"c\", \"b\"]\n{\"c\": 3, \"b\": 20}\n",
            Run.Program(source));
    }

    [Fact]
    public void ChangingACopyLeavesTheOriginalUnchanged()
    {
        const string source =
            "def list int a list_of int 1 2 end\ndef list int b a\nset b push b 3\nset b replace_at b 0 9\n" +
            "write_line a\nwrite_line b\n" +
            "def map string int m put empty map string int \"x\" 1\ndef map string int n m\n" +
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
            "    set m put m \"k\" push value_of m \"k\" 5\n    write_line m\nend\n" +
            "def list int a list_of int 1 end\ndef list int b grow a\nwrite_line a\nwrite_line b\n" +
            "def map string list int m put empty map string list int \"k\" list_of int 1 end\n" +
            "change m\nwrite_line m";

        Assert.Equal("[1]\n[1, 99]\n{\"k\": [1, 5]}\n{\"k\": [1]}\n", Run.Program(source));
    }

    [Fact]
    public void NestedCollectionsChangeOnlyThroughSet()
    {
        const string source =
            "def list list int grid list_of list int list_of int 0 0 end list_of int 0 0 end end\n" +
            "def list int row item_at grid 0\nset row replace_at row 1 5\n" +
            "write_line grid\nset grid replace_at grid 0 row\nwrite_line grid";

        Assert.Equal("[[0, 0], [0, 0]]\n[[0, 5], [0, 0]]\n", Run.Program(source));
    }

    [Theory]
    [InlineData("equal list_of int 1 2 end list_of int 1 2 end", "true")]
    [InlineData("equal list_of int 1 2 end list_of int 2 1 end", "false")]
    [InlineData("equal list_of int 1 end list_of int 1 1 end", "false")]
    [InlineData("equal empty list int list_of int end", "true")]
    [InlineData("equal empty map string int empty map string int", "true")]
    [InlineData("equal list_of list int list_of int 1 end empty list int end list_of list int list_of int 1 end list_of int end end", "true")]
    [InlineData("equal list_of list int list_of int 1 end end list_of list int list_of int 2 end end", "false")]
    [InlineData("equal put put empty map int int 1 1 2 2 put put empty map int int 2 2 1 1", "true")]
    [InlineData("equal put empty map int int 1 1 put empty map int int 1 2", "false")]
    [InlineData("equal put empty map int int 1 1 put empty map int int 2 1", "false")]
    [InlineData("equal remove_key put empty map int int 1 1 1 empty map int int", "true")]
    [InlineData("equal list_of float 0.0 end list_of float negate 0.0 end", "true")]
    [InlineData("equal put empty map string list int \"a\" list_of int 1 end put empty map string list int \"a\" list_of int 1 end", "true")]
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
    [InlineData("item_at list_of int 1 end \"0\"", "'item_at' cannot take list int string. It accepts: list T int (T stands for any type, the same wherever it appears).")]
    [InlineData("value_of put empty map string int \"a\" 1 1", "'value_of' cannot take map string int int. It accepts: map K V K (K and V stand for any types, each the same wherever it appears).")]
    [InlineData("length_of 5", "'length_of' cannot take int. It accepts: string, list T, map K V (T, K and V stand for any types, each the same wherever it appears).")]
    [InlineData("keys_of list_of int 1 end", "'keys_of' cannot take list int. It accepts: map K V (K and V stand for any types, each the same wherever it appears).")]
    public void RejectsOperandsOfTheWrongType(string expression, string message)
    {
        Assert.Equal(message, Failure($"write_line {expression}", ErrorStage.Type).Message);
    }

    [Fact]
    public void ResultTypesFollowTheElementType()
    {
        Assert.Equal("4", Evaluate("add item_at list_of int 1 2 end 1 2"));
        Assert.Equal("lamp!", Evaluate("add value_of put empty map int string 1 \"lamp\" 1 \"!\""));
        Assert.Equal("3", Evaluate("add item_at keys_of put empty map int bool 2 true 0 1"));

        var error = Failure("def string s item_at list_of int 1 end 0", ErrorStage.Type);
        Assert.Equal("Cannot initialize string variable 's' with a value of type int.", error.Message);
    }

    [Theory]
    [InlineData("item_at list_of int 1 2 3 end 3", "List index 3 is out of range for a list of 3 items, numbered from 0.")]
    [InlineData("item_at list_of int 1 end -1", "List index -1 is out of range for a list of 1 item, numbered from 0.")]
    [InlineData("item_at empty list int 0", "List index 0 is out of range for an empty list.")]
    [InlineData("replace_at list_of int 1 2 end 2 0", "List index 2 is out of range for a list of 2 items, numbered from 0.")]
    [InlineData("remove_at empty list string 0", "List index 0 is out of range for an empty list.")]
    [InlineData("item_at list_of int 1 end 9223372036854775807", "List index 9223372036854775807 is out of range for a list of 1 item, numbered from 0.")]
    [InlineData("value_of put empty map string int \"lamp\" 5 \"rope\"", "The map has no key \"rope\".")]
    [InlineData("value_of empty map int int 7", "The map has no key 7.")]
    [InlineData("value_of empty map bool int false", "The map has no key false.")]
    [InlineData("remove_key empty map string int \"a\\\"b\"", "The map has no key \"a\\\"b\".")]
    public void ReportsMissingItemsAtRuntime(string expression, string message)
    {
        var error = Failure($"write_line {expression}", ErrorStage.Runtime);
        Assert.Equal(message, error.Message);
        Assert.Equal(12, error.Position.Column);
    }

    [Theory]
    [InlineData("def map float int m empty map float int", 9, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def map list int int m empty map int int", 9, "A map key must be int, string, bool or a choice, found list int.")]
    [InlineData("def int m length_of empty map map int int string", 31, "A map key must be int, string, bool or a choice, found map int int.")]
    [InlineData("function int f map float int m return 0", 20, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def int x 0 write_line convert map float int x", 36, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def int x length_of list_of map float int end", 33, "A map key must be int, string, bool or a choice, found float.")]
    [InlineData("def list int x empty int", 22, "'empty' takes a list or map type, such as empty list string or empty map string int, found int.")]
    [InlineData("def list int x empty void", 22, "A collection cannot have type void.")]
    [InlineData("def list void x empty list int", 10, "A type argument cannot have type void.")]
    [InlineData("def list int x list_of void end", 24, "A list item cannot have type void.")]
    [InlineData("def list int x empty list", 26, "Expected a type, found end of input.")]
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
    public void ChecksTheDeclaredTypeOfACollection()
    {
        var error = Failure("def list float a list_of int 1 end", ErrorStage.Type);
        Assert.Equal("Cannot initialize list float variable 'a' with a value of type list int.", error.Message);
    }

    [Theory]
    [InlineData("def list int a list_of int 1 2 write_line a", "Expected a list item or 'end' to close the list_of started at")]
    [InlineData("write_line list_of int end end", "'end' without a matching 'block'.")]
    [InlineData("list_of int 1 end", "'list_of' cannot start a statement.")]
    [InlineData("empty list int", "'empty' cannot start a statement.")]
    [InlineData("def int list 0", "'list' is a reserved word and cannot be used as a variable name.")]
    [InlineData("def int push 0", "'push' is a reserved word and cannot be used as a variable name.")]
    [InlineData("def int empty 0", "'empty' is a reserved word and cannot be used as a variable name.")]
    public void ReportsMisplacedCollectionWords(string source, string message)
    {
        Assert.StartsWith(message, Failure(source, ErrorStage.Parse).Message);
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
    [InlineData("write_line empty map string", InputStatus.Incomplete)]
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
        session.Run("def list string bag empty list string");
        session.Run("set bag push bag \"lamp\"");
        Assert.Throws<PyropException>(() => session.Run("def list int bag empty list int"));
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
        "def map int int m put put put empty map int int 1 1 2 2 3 3\nset m put m 3 30\nwrite_line m\nset m put m 4 4",
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
            "def list int big empty list int\ndef int i 0\n" +
            "while less i 200000 block\n    set big push big i\n    set i add i 1\nend\n" +
            "def map int list int m put empty map int list int 1 big\n" +
            "set i 0\nwhile less i 2000 block\n    set m put m 1 replace_at value_of m 1 199999 i\n    set i add i 1\nend\n" +
            "write_line item_at value_of m 1 199999";

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
            "def list int a empty list int\ndef int i 0\n" +
            "while less i 100000 block\n    set a push a i\n    set i add i 1\nend\n" +
            "write_line length_of a\nwrite_line item_at a 99999";

        Run.Program(source);
        var watch = Stopwatch.StartNew();
        var output = Run.Program(source);
        watch.Stop();

        Assert.Equal("100000\n99999\n", output);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }
}
