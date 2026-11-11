namespace Pyrop.Tests;

public sealed class RenamedWordsTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("contains")]
    [InlineData("negate")]
    [InlineData("upper")]
    [InlineData("smaller")]
    [InlineData("substring")]
    [InlineData("def")]
    public void ARetiredWordIsAnOrdinaryName(string word)
    {
        Assert.Equal("3\n", Run.Program($"define int {word} 1\nfunction int twice_{word} int x return multiply 2 x\nwrite_line add {word} twice_{word} {word}"));
    }

    [Theory]
    [InlineData("write_line upper \"a\"", "'upper' is no longer part of Pyrop; it is now called uppercase.")]
    [InlineData("write_line lower \"A\"", "'lower' is no longer part of Pyrop; it is now called lowercase.")]
    [InlineData("write_line smaller 1 2", "'smaller' is no longer part of Pyrop; it is now called smaller_of.")]
    [InlineData("write_line larger 1 2", "'larger' is no longer part of Pyrop; it is now called larger_of.")]
    [InlineData("write_line substring \"abc\" 0 1", "'substring' is no longer part of Pyrop; use slice <text> <start> <count>.")]
    [InlineData("define int n 1\nwrite_line empty", "'empty' is no longer part of Pyrop; write an empty list as list_of int end and an empty map as map_of string int end.")]
    [InlineData("def int count 0", "'def' is no longer part of Pyrop; a variable is now declared with define, as in define int count 0.")]
    [InlineData("negate 1", "'negate' is no longer part of Pyrop; write subtract 0 <number>, or subtract 0.0 <number> for a float.")]
    public void AnUnknownRetiredWordSaysWhatReplacedIt(string source, string message)
    {
        Assert.Equal(message, Run.Failure(source).Message);
    }

    [Theory]
    [InlineData("use files library\nwrite_line exists \"a\"", "'exists' is now 'file_exists' in the library files")]
    [InlineData("use files library\nwrite_line listing \"a\"", "'listing' is now 'files_in' in the library files")]
    [InlineData("use files library\nwrite_line listing_all \"a\"", "'listing_all' is now 'files_under' in the library files")]
    [InlineData("use math library\nwrite_line abs 1", "'abs' is now 'absolute' in the library math")]
    [InlineData("use random library\nwrite_line between 1.0 2.0", "'between' is now 'float_between' in the library random")]
    [InlineData("use time library\nwrite_line plus_milliseconds 1 2", "'plus_milliseconds' is now 'add_milliseconds' in the library time")]
    public void AnOldLibraryNameSaysItsNewName(string source, string message)
    {
        Assert.StartsWith(message, Run.Failure(source).Message);
    }

    [Theory]
    [InlineData("write_line slice \"Pyrop\" 1 3", "yro")]
    [InlineData("write_line uppercase \"abc\"", "ABC")]
    [InlineData("write_line lowercase \"ABC\"", "abc")]
    [InlineData("write_line smaller_of 2 1", "1")]
    [InlineData("write_line larger_of 2 1", "2")]
    [InlineData("write_line values_of map_of string int \"b\" 2 \"a\" 1 end", "[2, 1]")]
    [InlineData("write_line values_of map_of int string end", "[]")]
    [InlineData("use math library\nwrite_line from math absolute -2", "2")]
    public void TheNewNamesWork(string source, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program(source));
    }

    [Theory]
    [InlineData("define int upper 1\nupper", "'upper' cannot stand alone as a statement.")]
    [InlineData("define int upper 1\nwrite_line add upper 1 upper", "'upper' cannot stand alone as a statement.")]
    [InlineData("function string upper string s return s\nwrite_line attempt upper \"a\"", "'attempt' takes a built-in operation or a convert")]
    [InlineData("function int f int upper return upper\nfunction int g return upper", "Undeclared variable 'upper'.")]
    public void ADeclaredRetiredWordIsTreatedLikeAnyOtherName(string source, string message)
    {
        Assert.StartsWith(message, Run.Failure(source).Message);
    }

    [Fact]
    public void ARetiredWordGlobalCanBeReadByAFunctionAboveIt()
    {
        Assert.Equal("7\n", Run.Program("function int read_it return smaller\ndefine int smaller 7\nwrite_line read_it"));
    }

    [Fact]
    public void ValuesOfFollowsTheKeysAfterChanges()
    {
        const string source = """
            define map string int m map_of string int "a" 1 "b" 2 "c" 3 end
            set m put m "a" 9
            set m remove m "b"
            set m put m "b" 4
            write_line keys_of m
            write_line values_of m
            """;

        Assert.Equal("[\"a\", \"c\", \"b\"]\n[9, 3, 4]\n", Run.Program(source));
    }

    [Fact]
    public void AnEqualsSignPointsToDefine()
    {
        Assert.EndsWith("write define or set to give a variable a value, and equal to compare.", Run.Failure("define int x 1\nset x = 2").Message);
    }

    [Fact]
    public void GreyIsSpelledGray()
    {
        var error = Run.Failure("use terminal library\nfrom terminal write_view from terminal in_color \"grey\" from terminal text \"x\"");

        Assert.Equal("Unknown color \"grey\"; it is spelled \"gray\".", error.Message);
        Assert.Equal("<gray>x\n</gray>", Run.Program("use terminal library\nfrom terminal write_view from terminal in_color \"gray\" from terminal text \"x\""));
    }
}
