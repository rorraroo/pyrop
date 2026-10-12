using System.Globalization;
using Pyrop.Runtime;

namespace Pyrop.Tests;

public class StandardLibraryTests
{
    private const string Sword = "\U0001F5E1\uFE0F";
    private const string DecomposedC = "c\u030C";

    private static string Evaluate(string expression) => Run.Program($"write_line {expression}").TrimEnd('\n');

    private static string RuntimeFailure(string expression)
    {
        var error = Run.Failure($"write_line {expression}");
        Assert.Equal(ErrorStage.Runtime, error.Stage);
        return error.Message;
    }

    [Theory]
    [InlineData("length_of \"čau\"", "3")]
    [InlineData("length_of \"\"", "0")]
    [InlineData("length_of \"a\\nb\"", "3")]
    [InlineData("length_of \"" + Sword + "x\"", "2")]
    [InlineData("length_of \"" + DecomposedC + "au\"", "3")]
    public void LengthOfCountsVisibleCharacters(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("substring \"Pyrop rocks\" 6 5", "rocks")]
    [InlineData("substring \"Pyrop rocks\" 0 5", "Pyrop")]
    [InlineData("substring \"abc\" 3 0", "")]
    [InlineData("substring \"" + Sword + "x\" 1 1", "x")]
    [InlineData("substring \"" + Sword + "x\" 0 1", Sword)]
    [InlineData("char_at \"čau\" 0", "č")]
    [InlineData("char_at \"" + DecomposedC + "au\" 0", DecomposedC)]
    [InlineData("char_at \"" + Sword + "x\" 1", "x")]
    public void TakesPartsOfTextByVisibleCharacter(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("substring \"abc\" 2 2", "Cannot take 2 characters from position 2 of a 3-character text.")]
    [InlineData("substring \"abc\" 4 0", "Position 4 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("substring \"abc\" -1 1", "Position -1 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("substring \"abc\" 0 -1", "'substring' cannot take a negative count (-1).")]
    [InlineData("char_at \"abc\" 3", "Position 3 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("char_at \"\" 0", "Position 0 is out of range for a 0-character text, numbered from 0.")]
    public void RejectsPositionsOutsideTheText(string expression, string message)
    {
        Assert.Equal(message, RuntimeFailure(expression));
    }

    [Theory]
    [InlineData("find \"hello\" \"l\"", "2")]
    [InlineData("find \"hello\" \"lo\"", "3")]
    [InlineData("find \"hello\" \"z\"", "-1")]
    [InlineData("find \"hello\" \"\"", "0")]
    [InlineData("find \"Hello\" \"h\"", "-1")]
    [InlineData("find \"" + Sword + "ab\" \"b\"", "2")]
    [InlineData("find \"" + DecomposedC + "c\" \"c\"", "1")]
    [InlineData("find \"" + DecomposedC + "\" \"c\"", "-1")]
    public void FindReturnsTheFirstWholeCharacterMatch(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("replace \"a-b-c\" \"-\" \"+\"", "a+b+c")]
    [InlineData("replace \"aaa\" \"aa\" \"b\"", "ba")]
    [InlineData("replace \"abc\" \"x\" \"y\"", "abc")]
    [InlineData("replace \"abc\" \"b\" \"\"", "ac")]
    [InlineData("replace \"" + DecomposedC + "c\" \"c\" \"x\"", DecomposedC + "x")]
    public void ReplaceSwapsEveryWholeCharacterMatch(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Fact]
    public void ReplaceRejectsAnEmptySearchText()
    {
        Assert.Equal("'replace' cannot search for an empty text.", RuntimeFailure("replace \"abc\" \"\" \"x\""));
    }

    [Theory]
    [InlineData("starts_with \"Pyrop\" \"Py\"", "true")]
    [InlineData("starts_with \"Pyrop\" \"py\"", "false")]
    [InlineData("starts_with \"Pyrop\" \"\"", "true")]
    [InlineData("ends_with \"report.txt\" \".txt\"", "true")]
    [InlineData("ends_with \"report.txt\" \".csv\"", "false")]
    [InlineData("ends_with \"" + DecomposedC + "\" \"\u030C\"", "false")]
    [InlineData("starts_with \"" + DecomposedC + "\" \"c\"", "false")]
    public void ChecksTheStartAndEndOfText(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("upper \"čau Pyrop\"", "ČAU PYROP")]
    [InlineData("lower \"ČAU Pyrop\"", "čau pyrop")]
    [InlineData("trim \"  x y \\t\"", "x y")]
    [InlineData("repeat \"ab\" 3", "ababab")]
    [InlineData("repeat \"ab\" 0", "")]
    [InlineData("pad_left \"7\" 3 \"0\"", "007")]
    [InlineData("pad_right \"ab\" 4 \".\"", "ab..")]
    [InlineData("pad_left \"abcd\" 2 \"0\"", "abcd")]
    [InlineData("pad_left \"x\" -5 \"0\"", "x")]
    [InlineData("pad_left \"" + Sword + "\" 3 \"" + DecomposedC + "\"", DecomposedC + DecomposedC + Sword)]
    public void TransformsText(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("repeat \"x\" -1", "'repeat' cannot take a negative count (-1).")]
    [InlineData("repeat \"ab\" 100000000", "The result of 'repeat' would exceed the largest allowed text size.")]
    [InlineData("pad_left \"a\" 9000000000 \"0\"", "The result of 'pad_left' would exceed the largest allowed text size.")]
    [InlineData("pad_left \"a\" 3 \"ab\"", "The fill for 'pad_left' must be exactly one character, found \"ab\".")]
    [InlineData("pad_right \"a\" 3 \"\"", "The fill for 'pad_right' must be exactly one character, found \"\".")]
    public void RejectsInvalidTextTransformations(string expression, string message)
    {
        Assert.Equal(message, RuntimeFailure(expression));
    }

    [Theory]
    [InlineData("abs -5", "5")]
    [InlineData("abs -2.5", "2.5")]
    [InlineData("negate 5", "-5")]
    [InlineData("negate -0.5", "0.5")]
    [InlineData("smaller 3 7", "3")]
    [InlineData("larger 2.5 1.5", "2.5")]
    [InlineData("smaller \"b\" \"a\"", "a")]
    [InlineData("larger \"b\" \"a\"", "b")]
    [InlineData("to_power 2 10", "1024")]
    [InlineData("to_power 2 62", "4611686018427387904")]
    [InlineData("to_power -3 3", "-27")]
    [InlineData("to_power 0 0", "1")]
    [InlineData("to_power -1 9223372036854775807", "-1")]
    [InlineData("to_power 2.0 0.5", "1.4142135623730951")]
    [InlineData("to_power 2.0 -1.0", "0.5")]
    [InlineData("square_root 9.0", "3.0")]
    [InlineData("at_least 3 3", "true")]
    [InlineData("at_least 2 3", "false")]
    [InlineData("at_most 2.0 1.0", "false")]
    [InlineData("at_most \"a\" \"b\"", "true")]
    public void ComputesNumbersAndComparisons(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("round 2.5", "3")]
    [InlineData("round -2.5", "-3")]
    [InlineData("round 2.4", "2")]
    [InlineData("round_down 2.7", "2")]
    [InlineData("round_down -2.5", "-3")]
    [InlineData("round_down 3.0", "3")]
    public void RoundsFloatsToInts(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("abs -9223372036854775808", "Integer overflow.")]
    [InlineData("negate -9223372036854775808", "Integer overflow.")]
    [InlineData("to_power 2 63", "Integer overflow.")]
    [InlineData("to_power 2 -1", "'to_power' on ints cannot take a negative exponent (-1); use floats instead.")]
    [InlineData("to_power -8.0 0.5", "Float result is not a real number.")]
    [InlineData("to_power 10.0 400.0", "Float result is out of range.")]
    [InlineData("square_root -1.0", "Cannot take the square root of a negative number (-1.0).")]
    [InlineData("round to_power 10.0 30.0", "Float 1E+30 is out of range for int.")]
    public void RejectsMathWithoutAResult(string expression, string message)
    {
        Assert.Equal(message, RuntimeFailure(expression));
    }

    [Fact]
    public void SeedRandomMakesRandomnessReproducible()
    {
        const string source = "seed_random 42\nwrite_line roll_dice 1 1000000\nwrite_line roll_dice 1 1000000\nwrite_line random 0.0 1.0";

        Assert.Equal("558743\n543103\n0.6800434110281394\n", Run.Program(source));
    }

    [Fact]
    public void SeedRandomRestartsTheSequence()
    {
        var output = Run.Program("seed_random -7\nwrite_line roll_dice 1 1000000\nseed_random -7\nwrite_line roll_dice 1 1000000");

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(lines[0], lines[1]);
    }

    [Fact]
    public void RollDiceCoversItsInclusiveRange()
    {
        var output = Run.Program("def int count 0\nwhile less count 600 block\n    write_line roll_dice 1 6\n    set count add count 1\nend");

        var rolls = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6], rolls.Distinct().Order());
    }

    [Theory]
    [InlineData("roll_dice 5 5")]
    [InlineData("roll_dice -9223372036854775808 9223372036854775807")]
    [InlineData("roll_dice -3 -1")]
    public void RollDiceAcceptsAnyOrderedBounds(string expression)
    {
        var roll = long.Parse(Evaluate(expression), CultureInfo.InvariantCulture);

        var bounds = expression.Split(' ').Skip(1).Select(bound => long.Parse(bound, CultureInfo.InvariantCulture)).ToArray();
        Assert.InRange(roll, bounds[0], bounds[1]);
    }

    [Fact]
    public void RandomStaysBelowItsUpperBound()
    {
        var output = Run.Program("def int count 0\nwhile less count 500 block\n    write_line random 2.0 3.0\n    set count add count 1\nend");

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = double.Parse(line, CultureInfo.InvariantCulture);
            Assert.True(value is >= 2.0 and < 3.0, $"{value} is outside [2, 3).");
        }
    }

    [Theory]
    [InlineData("roll_dice 6 1", "'roll_dice' needs a lower bound no greater than its upper bound, found 6 and 1.")]
    [InlineData("random 1.0 1.0", "'random' needs a lower bound below its upper bound, found 1.0 and 1.0.")]
    public void RejectsReversedRandomBounds(string expression, string message)
    {
        Assert.Equal(message, RuntimeFailure(expression));
    }

    [Fact]
    public void TheGeneratorMatchesTheXoshiro256StarStarReference()
    {
        var source = new RandomSource(1, 2, 3, 4);

        Assert.Equal(
            [11520UL, 0UL, 1509978240UL, 1215971899390074240UL],
            Enumerable.Range(0, 4).Select(_ => source.NextBits()));
    }

    [Fact]
    public void SeedingUsesTheSplitMix64Reference()
    {
        var source = new RandomSource(0);

        const ulong secondSplitMixOutput = 0x6E789E6AA1B965F4;
        Assert.Equal(System.Numerics.BitOperations.RotateLeft(unchecked(secondSplitMixOutput * 5), 7) * 9, source.NextBits());
    }

    [Fact]
    public void LongTextsGiveTheSameAnswersAsShortOnes()
    {
        var text = string.Concat(Enumerable.Repeat("č" + DecomposedC + Sword + "ab ", 60));
        var host = new HostContext([text], _ => null);

        var output = Run.Program(
            "def string text argument_at 0\n" +
            "write_line length_of text\nwrite_line char_at text 241\nwrite_line find text \"b \"\n" +
            "write_line substring text 237 3\nwrite_line length_of replace text \"c\" \"x\"",
            host: host);

        Assert.Equal($"360\n{DecomposedC}\n4\nab \n360\n", output);
    }

    [Theory]
    [InlineData("repeat \"\" 3000000000", "")]
    [InlineData("repeat \"\" 9223372036854775807", "")]
    [InlineData("pad_left \"abc\" -9223372036854775808 \"x\"", "abc")]
    [InlineData("trim \" ́x \"", " ́x")]
    [InlineData("trim \"\\t\\n\"", "")]
    public void HandlesExtremeAndUnusualArguments(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("́")]
    [InlineData("\U0001F1E8")]
    public void RejectsAFillThatMergesWithItsNeighbours(string fill)
    {
        var host = new HostContext([fill], _ => null);

        var error = Run.Failure("write_line pad_right \"a\" 3 argument_at 0", host: host);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains("merges with the characters next to it", error.Message);
    }

    [Fact]
    public void SeedRandomIsAStatement()
    {
        var error = Run.Failure("write_line seed_random 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("Operation 'seed_random' returns void and cannot be used as a value.", error.Message);
    }

    [Theory]
    [InlineData("def int lower 1")]
    [InlineData("def string find \"x\"")]
    public void ReservesTheNewNames(string source)
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure(source).Stage);
    }

    [Fact]
    public void ChecksOperandTypes()
    {
        var error = Run.Failure("write_line square_root 9");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'square_root' cannot take int. It accepts: float.", error.Message);
    }
}
