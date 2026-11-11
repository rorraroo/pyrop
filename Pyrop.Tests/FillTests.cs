namespace Pyrop.Tests;

public class FillTests
{
    [Theory]
    [InlineData("fill \"You won in {} moves.\" list_of int 12 end", "You won in 12 moves.")]
    [InlineData("fill \"{} hits {}\" list_of string \"Ann\" \"the rat\" end", "Ann hits the rat")]
    [InlineData("fill \"{}{}\" list_of float 1.0 2.5 end", "1.02.5")]
    [InlineData("fill \"no places\" list_of int end", "no places")]
    [InlineData("fill \"\" list_of int end", "")]
    [InlineData("fill \"{{}} and {{ and }} {}\" list_of bool true end", "{} and { and } true")]
    [InlineData("fill \"{}\" list_of list string list_of string \"a\" end end", "[\"a\"]")]
    [InlineData("fill \"{}\" list_of option int some 1 end", "some 1")]
    [InlineData("fill \"č{}ř\" list_of string \"{}\" end", "č{}ř")]
    public void FillsTheTemplateWithTheItemsInOrder(string expression, string expected)
    {
        Assert.Equal(expected + "\n", Run.Program($"write_line {expression}"));
    }

    [Fact]
    public void FillWritesRecordsAndChoicesInTheirTextForm()
    {
        const string source = """
            record Point int x int y end
            choice Move go int stay end
            write_line fill "at {}" list_of Point Point 1 2 end
            write_line fill "{} then {}" list_of Move go 3 stay end
            """;

        Assert.Equal("at Point x 1 y 2\ngo 3 then stay\n", Run.Program(source));
    }

    [Theory]
    [InlineData("fill \"{} {}\" list_of int 1 end", "The template of 'fill' has 2 places for items, but the list has 1 item.")]
    [InlineData("fill \"{}\" list_of int 1 2 end", "The template of 'fill' has 1 place for items, but the list has 2 items.")]
    [InlineData("fill \"none\" list_of int 1 end", "The template of 'fill' has 0 places for items, but the list has 1 item.")]
    [InlineData("fill \"a { b\" list_of int end",
        "The template of 'fill' has a lone '{' at position 2; write {} for an item, and {{ or }} for a brace of its own.")]
    [InlineData("fill \"č}\" list_of int end",
        "The template of 'fill' has a lone '}' at position 1; write {} for an item, and {{ or }} for a brace of its own.")]
    [InlineData("fill \"{\" list_of int end",
        "The template of 'fill' has a lone '{' at position 0; write {} for an item, and {{ or }} for a brace of its own.")]
    public void FillFailsOnABadTemplate(string expression, string message)
    {
        var error = Run.Failure($"write_line {expression}");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
        Assert.Equal("absent\n", Run.Program($"if_some string s attempt {expression} write_line s else write_line \"absent\""));
    }

    [Fact]
    public void FillTakesAStringAndAList()
    {
        var error = Run.Failure("write_line fill 1 list_of int end");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'fill' cannot take int list int. It accepts: string list T (T stands for any type, the same wherever it appears).", error.Message);
    }
}
