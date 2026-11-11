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

    private const string NumberLibraries =
        "use math library\nimport from math absolute sign power square_root round round_down end\n" +
        "use random library\nimport from random float_between roll seed end\n";

    private static string EvaluateNumber(string expression) => Run.Program(NumberLibraries + $"write_line {expression}").TrimEnd('\n');

    private static string NumberFailure(string expression)
    {
        var error = Run.Failure(NumberLibraries + $"write_line {expression}");
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
    [InlineData("slice \"Pyrop rocks\" 6 5", "rocks")]
    [InlineData("slice \"Pyrop rocks\" 0 5", "Pyrop")]
    [InlineData("slice \"abc\" 3 0", "")]
    [InlineData("slice \"" + Sword + "x\" 1 1", "x")]
    [InlineData("slice \"" + Sword + "x\" 0 1", Sword)]
    [InlineData("get \"čau\" 0", "č")]
    [InlineData("get \"" + DecomposedC + "au\" 0", DecomposedC)]
    [InlineData("get \"" + Sword + "x\" 1", "x")]
    public void TakesPartsOfTextByVisibleCharacter(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("slice \"abc\" 2 2", "Cannot take 2 characters from position 2 of a 3-character text.")]
    [InlineData("slice \"abc\" 4 0", "Position 4 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("slice \"abc\" -1 1", "Position -1 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("slice \"abc\" 0 -1", "'slice' cannot take a negative count (-1).")]
    [InlineData("get \"abc\" 3", "Position 3 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("get \"\" 0", "Position 0 is out of range for a 0-character text, numbered from 0.")]
    public void RejectsPositionsOutsideTheText(string expression, string message)
    {
        Assert.Equal(message, RuntimeFailure(expression));
    }

    [Theory]
    [InlineData("find \"hello\" \"l\"", "some 2")]
    [InlineData("find \"hello\" \"lo\"", "some 3")]
    [InlineData("find \"hello\" \"z\"", "none")]
    [InlineData("find \"hello\" \"\"", "some 0")]
    [InlineData("find \"Hello\" \"h\"", "none")]
    [InlineData("find \"" + Sword + "ab\" \"b\"", "some 2")]
    [InlineData("find \"" + DecomposedC + "c\" \"c\"", "some 1")]
    [InlineData("find \"" + DecomposedC + "\" \"c\"", "none")]
    public void FindReturnsTheFirstWholeCharacterMatch(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("find_from \"a-b-c\" \"-\" 0", "some 1")]
    [InlineData("find_from \"a-b-c\" \"-\" 2", "some 3")]
    [InlineData("find_from \"a-b-c\" \"-\" 4", "none")]
    [InlineData("find_from \"abc\" \"\" 3", "some 3")]
    [InlineData("find_from \"" + Sword + "a" + Sword + "a\" \"a\" 2", "some 3")]
    [InlineData("find_from \"" + DecomposedC + "cc\" \"c\" 0", "some 1")]
    public void FindFromSearchesFromAPosition(string expression, string expected)
    {
        Assert.Equal(expected, Evaluate(expression));
    }

    [Theory]
    [InlineData("find_from \"abc\" \"a\" 4", "Position 4 is out of range for a 3-character text, numbered from 0.")]
    [InlineData("find_from \"abc\" \"a\" -1", "Position -1 is out of range for a 3-character text, numbered from 0.")]
    public void FindFromRejectsAStartOutsideTheText(string expression, string message)
    {
        Assert.Equal(message, RuntimeFailure(expression));
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
    [InlineData("uppercase \"čau Pyrop\"", "ČAU PYROP")]
    [InlineData("lowercase \"ČAU Pyrop\"", "čau pyrop")]
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
    [InlineData("absolute -5", "5")]
    [InlineData("absolute -2.5", "2.5")]
    [InlineData("smaller_of 3 7", "3")]
    [InlineData("larger_of 2.5 1.5", "2.5")]
    [InlineData("smaller_of \"b\" \"a\"", "a")]
    [InlineData("larger_of \"b\" \"a\"", "b")]
    [InlineData("power 2 10", "1024")]
    [InlineData("power 2 62", "4611686018427387904")]
    [InlineData("power -3 3", "-27")]
    [InlineData("power 0 0", "1")]
    [InlineData("power -1 9223372036854775807", "-1")]
    [InlineData("power 2.0 0.5", "1.4142135623730951")]
    [InlineData("power 2.0 -1.0", "0.5")]
    [InlineData("square_root 9.0", "3.0")]
    [InlineData("at_least 3 3", "true")]
    [InlineData("at_least 2 3", "false")]
    [InlineData("at_most 2.0 1.0", "false")]
    [InlineData("at_most \"a\" \"b\"", "true")]
    public void ComputesNumbersAndComparisons(string expression, string expected)
    {
        Assert.Equal(expected, EvaluateNumber(expression));
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
        Assert.Equal(expected, EvaluateNumber(expression));
    }

    [Theory]
    [InlineData("absolute -9223372036854775808", "Integer overflow.")]
    [InlineData("power 2 63", "Integer overflow.")]
    [InlineData("power 2 -1", "'power' on ints cannot take a negative exponent (-1); use floats instead.")]
    [InlineData("power -8.0 0.5", "Float result is not a real number.")]
    [InlineData("power 10.0 400.0", "Float result is out of range.")]
    [InlineData("square_root -1.0", "Cannot take the square root of a negative number (-1.0).")]
    [InlineData("round power 10.0 30.0", "Float 1E+30 is out of range for int.")]
    public void RejectsMathWithoutAResult(string expression, string message)
    {
        Assert.Equal(message, NumberFailure(expression));
    }

    [Fact]
    public void SeedMakesRandomnessReproducible()
    {
        const string source = NumberLibraries + "seed 42\nwrite_line roll 1 1000000\nwrite_line roll 1 1000000\nwrite_line float_between 0.0 1.0";

        Assert.Equal("558743\n543103\n0.6800434110281394\n", Run.Program(source));
    }

    [Fact]
    public void SeedRestartsTheSequence()
    {
        var output = Run.Program(NumberLibraries + "seed -7\nwrite_line roll 1 1000000\nseed -7\nwrite_line roll 1 1000000");

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(lines[0], lines[1]);
    }

    [Fact]
    public void RollCoversItsInclusiveRange()
    {
        var output = Run.Program(NumberLibraries + "define int count 0\nwhile less count 600 block\n    write_line roll 1 6\n    set count add count 1\nend");

        var rolls = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6], rolls.Distinct().Order());
    }

    [Theory]
    [InlineData("roll 5 5")]
    [InlineData("roll -9223372036854775808 9223372036854775807")]
    [InlineData("roll -3 -1")]
    public void RollAcceptsAnyOrderedBounds(string expression)
    {
        var roll = long.Parse(EvaluateNumber(expression), CultureInfo.InvariantCulture);

        var bounds = expression.Split(' ').Skip(1).Select(bound => long.Parse(bound, CultureInfo.InvariantCulture)).ToArray();
        Assert.InRange(roll, bounds[0], bounds[1]);
    }

    [Fact]
    public void BetweenStaysBelowItsUpperBound()
    {
        var output = Run.Program(NumberLibraries + "define int count 0\nwhile less count 500 block\n    write_line float_between 2.0 3.0\n    set count add count 1\nend");

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var value = double.Parse(line, CultureInfo.InvariantCulture);
            Assert.True(value is >= 2.0 and < 3.0, $"{value} is outside [2, 3).");
        }
    }

    [Theory]
    [InlineData("roll 6 1", "'roll' needs a lower bound no greater than its upper bound, found 6 and 1.")]
    [InlineData("float_between 1.0 1.0", "'float_between' needs a lower bound below its upper bound, found 1.0 and 1.0.")]
    public void RejectsReversedRandomBounds(string expression, string message)
    {
        Assert.Equal(message, NumberFailure(expression));
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
            "use program library\ndefine string text get from program arguments 0\n" +
            "write_line length_of text\nwrite_line get text 241\nwrite_line find text \"b \"\n" +
            "write_line slice text 237 3\nwrite_line length_of replace text \"c\" \"x\"",
            host: host);

        Assert.Equal($"360\n{DecomposedC}\nsome 4\nab \n360\n", output);
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

        var error = Run.Failure("use program library\nwrite_line pad_right \"a\" 3 get from program arguments 0", host: host);

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Contains("merges with the characters next to it", error.Message);
    }

    [Fact]
    public void SeedIsAStatement()
    {
        var error = Run.Failure(NumberLibraries + "write_line seed 1");

        Assert.Equal(ErrorStage.Parse, error.Stage);
        Assert.Equal("Operation 'seed' returns void and cannot be used as a value.", error.Message);
    }

    [Theory]
    [InlineData("define int lowercase 1")]
    [InlineData("define string find \"x\"")]
    public void ReservesTheNewNames(string source)
    {
        Assert.Equal(ErrorStage.Parse, Run.Failure(source).Stage);
    }

    [Fact]
    public void ChecksOperandTypes()
    {
        var error = Run.Failure(NumberLibraries + "write_line square_root 9");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal("'square_root' cannot take int. It accepts: float.", error.Message);
    }

    [Theory]
    [InlineData("sign 5", "1")]
    [InlineData("sign -3", "-1")]
    [InlineData("sign 0", "0")]
    [InlineData("sign subtract -9223372036854775807 1", "-1")]
    [InlineData("sign 2.5", "1.0")]
    [InlineData("sign -0.5", "-1.0")]
    [InlineData("sign 0.0", "0.0")]
    [InlineData("sign -0.0", "0.0")]
    public void SignGivesTheDirectionOfANumberInItsOwnType(string expression, string expected)
    {
        Assert.Equal(expected, EvaluateNumber(expression));
    }
}
