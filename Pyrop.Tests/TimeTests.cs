using Pyrop.Runtime;

namespace Pyrop.Tests;

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;

    public DateTimeOffset UtcNow => Now.ToUniversalTime();

    public int Waited { get; private set; }

    public TimeSpan OffsetAt(DateTime time) => Now.Offset;

    public void Wait(int milliseconds)
    {
        Waited += milliseconds;
        Now = Now.AddMilliseconds(milliseconds);
    }
}

public class TimeTests
{
    private const string Imports =
        "use time library\nimport from time Moment now now_utc moment_text parse_moment milliseconds_between plus_milliseconds is_before wait end\n";

    private static readonly DateTimeOffset Noon = new(2026, 11, 9, 12, 30, 15, 250, TimeSpan.FromHours(1));

    private static HostContext At(IClock clock) => HostContext.Isolated with { Clock = clock };

    private static string Program(string source, IClock? clock = null) =>
        Run.Program(Imports + source, host: At(clock ?? new FixedClock(Noon)));

    private static PyropError Failure(string source) =>
        Run.Failure(Imports + source, host: At(new FixedClock(Noon)));

    [Fact]
    public void NowIsAMomentWithNamedFields()
    {
        const string source =
            "def Moment today now\n" +
            "write_line list_of int the year of today the month of today the day of today the hour of today the minute of today the second of today the millisecond of today the offset_minutes of today end";

        Assert.Equal("[2026, 11, 9, 12, 30, 15, 250, 60]\n", Program(source));
    }

    [Fact]
    public void NowUtcIsTheSameInstantAtOffsetZero()
    {
        const string source =
            "def Moment there now_utc\nwrite_line the hour of there\nwrite_line the offset_minutes of there\nwrite_line milliseconds_between now there";

        Assert.Equal("11\n0\n0\n", Program(source));
    }

    [Fact]
    public void WaitGoesThroughTheClock()
    {
        var clock = new FixedClock(Noon);

        Assert.Equal("1500\n", Program("def Moment a now\nwait 1500\nwrite_line milliseconds_between a now", clock));
        Assert.Equal(1500, clock.Waited);
    }

    [Theory]
    [InlineData("-1", "'wait' needs a time from 0 to 2147483647 milliseconds, found -1.")]
    [InlineData("2147483648", "'wait' needs a time from 0 to 2147483647 milliseconds, found 2147483648.")]
    public void WaitRefusesATimeItCannotKeep(string count, string message)
    {
        var error = Failure($"wait {count}");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("{year}-{month}-{day}", "2026-11-09")]
    [InlineData("{hour}:{minute}:{second}.{millisecond}", "12:30:15.250")]
    [InlineData("{offset}", "+01:00")]
    [InlineData("{weekday} {day} {month_name}", "Monday 09 November")]
    [InlineData("{{{year}}}", "{2026}")]
    [InlineData("no parts", "no parts")]
    public void WritesAMomentByLayout(string layout, string expected)
    {
        Assert.Equal(expected + "\n", Program($"write_line moment_text now \"{layout}\""));
    }

    [Fact]
    public void WritesANegativeOffset()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromMinutes(-330)));

        Assert.Equal("-05:30\n", Program("write_line moment_text now \"{offset}\"", clock));
    }

    [Theory]
    [InlineData("2026-11-09", "{year}-{month}-{day}", "[2026, 11, 9, 0, 0, 0, 0, 60]")]
    [InlineData("2026-1-5", "{year}-{month}-{day}", "[2026, 1, 5, 0, 0, 0, 0, 60]")]
    [InlineData("20260105", "{year}{month}{day}", "[2026, 1, 5, 0, 0, 0, 0, 60]")]
    [InlineData("2026", "{year}", "[2026, 1, 1, 0, 0, 0, 0, 60]")]
    [InlineData("2026-03-04T05:06:07.008+02:30", "{year}-{month}-{day}T{hour}:{minute}:{second}.{millisecond}{offset}", "[2026, 3, 4, 5, 6, 7, 8, 150]")]
    [InlineData("2026-03-04 Z", "{year}-{month}-{day} {offset}", "[2026, 3, 4, 0, 0, 0, 0, 0]")]
    [InlineData("{2026}", "{{{year}}}", "[2026, 1, 1, 0, 0, 0, 0, 60]")]
    public void ReadsAMomentByLayout(string text, string layout, string expected)
    {
        var source =
            $"def Moment m parse_moment \"{text}\" \"{layout}\"\n" +
            "write_line list_of int the year of m the month of m the day of m the hour of m the minute of m the second of m the millisecond of m the offset_minutes of m end";

        Assert.Equal(expected + "\n", Program(source));
    }

    [Fact]
    public void WrittenTextReadsBackToAnEqualMoment()
    {
        const string layout = "{year}-{month}-{day} {hour}:{minute}:{second}.{millisecond} {offset}";

        Assert.Equal("true\n", Program($"write_line equal now parse_moment moment_text now \"{layout}\" \"{layout}\""));
    }

    [Fact]
    public void AMomentSurvivesConvertToTextAndBack()
    {
        Assert.Equal("true\n", Program("write_line equal now convert Moment convert string now"));
    }

    [Theory]
    [InlineData("2026-13", "{year}-{month}", "\"2026-13\" is not a real date and time.")]
    [InlineData("2026-02-30", "{year}-{month}-{day}", "\"2026-02-30\" is not a real date and time.")]
    [InlineData("hello", "{year}", "\"hello\" does not fit the layout \"{year}\".")]
    [InlineData("2026 extra", "{year}", "\"2026 extra\" does not fit the layout \"{year}\".")]
    [InlineData("2026/11", "{year}-{month}", "\"2026/11\" does not fit the layout \"{year}-{month}\".")]
    [InlineData("2026+15:00", "{year}{offset}", "\"2026+15:00\" does not fit the layout \"{year}{offset}\".")]
    [InlineData("2026+00:99", "{year}{offset}", "\"2026+00:99\" does not fit the layout \"{year}{offset}\".")]
    public void TextThatDoesNotFitIsARuntimeError(string text, string layout, string message)
    {
        var error = Failure($"write_line parse_moment \"{text}\" \"{layout}\"");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void AttemptCatchesTextThatDoesNotFit()
    {
        Assert.Equal("no\n", Program("if_some Moment m attempt parse_moment \"x\" \"{year}\" write_line \"yes\"\nelse write_line \"no\""));
    }

    [Theory]
    [InlineData("moment_text now \"{yeer}\"", "There is no part {yeer} in a time layout; the parts are year, month, day, hour, minute, second, millisecond, offset, month_name and weekday.")]
    [InlineData("moment_text now \"{year\"", "The time layout \"{year\" has an unmatched brace; write {{ and }} for literal braces.")]
    [InlineData("moment_text now \"year}\"", "The time layout \"year}\" has an unmatched brace; write {{ and }} for literal braces.")]
    [InlineData("parse_moment \"x\" \"{year} {weekday}\"", "The part {weekday} can only be written, so it cannot be used by 'parse_moment'.")]
    [InlineData("parse_moment \"x\" \"{year} {year}\"", "The part {year} appears twice in the time layout \"{year} {year}\".")]
    [InlineData("parse_moment \"x\" \"{month}\"", "The time layout \"{month}\" needs {year} to be read.")]
    public void ALiteralLayoutIsCheckedBeforeTheProgramRuns(string expression, string message)
    {
        var error = Failure($"write_line 1\nwrite_line {expression}");

        Assert.Equal(ErrorStage.Type, error.Stage);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void ALayoutThatIsNotALiteralIsCheckedWhenUsed()
    {
        var error = Failure("def string layout \"{nope}\"\nwrite_line moment_text now layout");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.StartsWith("There is no part {nope}", error.Message);
    }

    [Fact]
    public void ComparesInstantsAcrossOffsets()
    {
        const string source =
            "def Moment east parse_moment \"2026-06-01 12:00 +02:00\" \"{year}-{month}-{day} {hour}:{minute} {offset}\"\n" +
            "def Moment west parse_moment \"2026-06-01 11:30 +00:00\" \"{year}-{month}-{day} {hour}:{minute} {offset}\"\n" +
            "write_line milliseconds_between east west\nwrite_line is_before east west\nwrite_line is_before west east\nwrite_line is_before east east";

        Assert.Equal("5400000\ntrue\nfalse\nfalse\n", Program(source));
    }

    [Fact]
    public void PlusMillisecondsMovesAndKeepsTheOffset()
    {
        Assert.Equal(
            "2026-11-10 12:30 +01:00\n2026-11-08 12:30 +01:00\n",
            Program(
                "write_line moment_text plus_milliseconds now 86400000 \"{year}-{month}-{day} {hour}:{minute} {offset}\"\n" +
                "write_line moment_text plus_milliseconds now -86400000 \"{year}-{month}-{day} {hour}:{minute} {offset}\""));
    }

    [Theory]
    [InlineData("Moment 2026 13 1 0 0 0 0 0", "month is 13")]
    [InlineData("Moment 2026 2 30 0 0 0 0 0", "day is 30")]
    [InlineData("Moment 0 1 1 0 0 0 0 0", "year is 0")]
    [InlineData("Moment 2026 1 1 24 0 0 0 0", "hour is 24")]
    [InlineData("Moment 2026 1 1 0 60 0 0 0", "minute is 60")]
    [InlineData("Moment 2026 1 1 0 0 60 0 0", "second is 60")]
    [InlineData("Moment 2026 1 1 0 0 0 1000 0", "millisecond is 1000")]
    [InlineData("Moment 2026 1 1 0 0 0 0 900", "offset_minutes is 900")]
    public void AnUnrealMomentFailsWhereItIsUsed(string moment, string detail)
    {
        var error = Failure($"def Moment m {moment}\nwrite_line is_before m now");

        Assert.Equal(ErrorStage.Runtime, error.Stage);
        Assert.Equal($"This is not a real moment: its {detail}.", error.Message);
    }

    [Fact]
    public void AnUnrealMonthDoesNotCrashTheDayCheck()
    {
        Assert.Contains("month is 0", Failure("write_line is_before Moment 2026 0 1 0 0 0 0 0 now").Message);
    }

    [Fact]
    public void MovingOutOfTheYearsIsARuntimeError()
    {
        Assert.Equal(
            "Moving this moment by 9223372036854775807 milliseconds leaves the years 1 to 9999.",
            Failure("write_line plus_milliseconds now 9223372036854775807").Message);
    }

    [Fact]
    public void AMomentAtTheEdgeOfTheYearsFailsInsteadOfCrashing()
    {
        Assert.Equal(
            "This is not a real moment: in UTC it falls outside the years 1 to 9999.",
            Failure("write_line is_before Moment 1 1 1 0 0 0 0 60 now").Message);
    }

    [Fact]
    public void TheNamesAreFreeWithoutTheLibrary()
    {
        Assert.Equal("1\n", Run.Program("def int now 1\nwrite_line now"));
    }
}
