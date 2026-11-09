using System.Globalization;
using System.Text;
using Pyrop.Parsing;

namespace Pyrop.Runtime;

public static class TimeOperations
{
    private const string Year = "year";
    private const string Month = "month";
    private const string Day = "day";
    private const string Hour = "hour";
    private const string Minute = "minute";
    private const string Second = "second";
    private const string Millisecond = "millisecond";
    private const string Offset = "offset";
    private const string MonthName = "month_name";
    private const string Weekday = "weekday";
    private const int MostOffsetMinutes = 14 * 60;

    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly string[] WeekdayNames =
        ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    private static readonly Dictionary<string, int> NumberWidths = new()
    {
        [Year] = 4,
        [Month] = 2,
        [Day] = 2,
        [Hour] = 2,
        [Minute] = 2,
        [Second] = 2,
        [Millisecond] = 3,
    };

    private static readonly string[] PartNames = [.. NumberWidths.Keys, Offset, MonthName, Weekday];

    private sealed record LayoutPiece(string Text, bool IsPart);

    public static RecordType DeclareMoment(Module module)
    {
        var position = new SourcePosition(0, 0);
        var record = new RecordType("Moment", module, position, isExported: true);
        record.Define(
            new[] { Year, Month, Day, Hour, Minute, Second, Millisecond, "offset_minutes" }
                .Select(field => new RecordField(PyropType.Int, field, position))
                .ToList());
        return record;
    }

    public static Value MomentOf(DateTimeOffset moment) =>
        new RecordValue(LibraryOperations.Moment,
        [
            new IntValue(moment.Year),
            new IntValue(moment.Month),
            new IntValue(moment.Day),
            new IntValue(moment.Hour),
            new IntValue(moment.Minute),
            new IntValue(moment.Second),
            new IntValue(moment.Millisecond),
            new IntValue((long)moment.Offset.TotalMinutes),
        ]);

    public static Value LocalMomentOf(IBuiltinInvocation invocation, DateTime utc) =>
        MomentOf(new DateTimeOffset(utc).ToOffset(invocation.Host.Clock.OffsetAt(utc)));

    public static string? LiteralLayoutProblem(BuiltinOperation operation, string layout)
    {
        if (LibraryOperations.Time.Operations.GetValueOrDefault(operation.Name) != operation)
        {
            return null;
        }

        return operation.Name switch
        {
            "moment_text" => ProblemIn(layout, reading: false),
            "parse_moment" => ProblemIn(layout, reading: true),
            _ => null,
        };
    }

    public static string Text(IBuiltinInvocation invocation, Value moment, string layout)
    {
        var time = Instant(invocation, moment);
        var builder = new StringBuilder();
        foreach (var piece in PiecesOf(invocation, layout, reading: false))
        {
            builder.Append(piece.IsPart ? PartOf(time, piece.Text) : piece.Text);
        }

        return builder.ToString();
    }

    public static Value Parse(IBuiltinInvocation invocation, string text, string layout)
    {
        var misfit = () => invocation.Fail($"\"{text}\" does not fit the layout \"{layout}\".");
        var numbers = new Dictionary<string, int>();
        TimeSpan? offset = null;
        var position = 0;
        foreach (var piece in PiecesOf(invocation, layout, reading: true))
        {
            if (!piece.IsPart)
            {
                if (string.CompareOrdinal(text, position, piece.Text, 0, piece.Text.Length) != 0)
                {
                    throw misfit();
                }

                position += piece.Text.Length;
            }
            else if (piece.Text == Offset)
            {
                offset = ReadOffset(text, ref position) ?? throw misfit();
            }
            else
            {
                numbers[piece.Text] = ReadNumber(text, ref position, NumberWidths[piece.Text]) ?? throw misfit();
            }
        }

        if (position != text.Length)
        {
            throw misfit();
        }

        int Part(string name, int absent) => numbers.GetValueOrDefault(name, absent);

        try
        {
            var written = new DateTime(
                Part(Year, 1), Part(Month, 1), Part(Day, 1), Part(Hour, 0), Part(Minute, 0), Part(Second, 0), Part(Millisecond, 0), DateTimeKind.Unspecified);
            return MomentOf(new DateTimeOffset(written, offset ?? invocation.Host.Clock.OffsetAt(written)));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw invocation.Fail($"\"{text}\" is not a real date and time.");
        }
    }

    public static long MillisecondsBetween(IBuiltinInvocation invocation, Value earlier, Value later) =>
        (Instant(invocation, later) - Instant(invocation, earlier)).Ticks / TimeSpan.TicksPerMillisecond;

    public static Value PlusMilliseconds(IBuiltinInvocation invocation, Value moment, long count)
    {
        var start = Instant(invocation, moment);
        try
        {
            return MomentOf(start.AddTicks(checked(count * TimeSpan.TicksPerMillisecond)));
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            throw invocation.Fail($"Moving this moment by {count} milliseconds leaves the years 1 to 9999.");
        }
    }

    public static bool IsBefore(IBuiltinInvocation invocation, Value first, Value second) =>
        Instant(invocation, first) < Instant(invocation, second);

    public static Value? Wait(IBuiltinInvocation invocation, long milliseconds)
    {
        if (milliseconds is < 0 or > int.MaxValue)
        {
            throw invocation.Fail($"'wait' needs a time from 0 to {int.MaxValue} milliseconds, found {milliseconds}.");
        }

        invocation.Host.Clock.Wait((int)milliseconds);
        return null;
    }

    private static DateTimeOffset Instant(IBuiltinInvocation invocation, Value moment)
    {
        var fields = moment.AsRecord().Fields;
        long Field(int index) => fields[index].AsInt();

        void Require(string name, long value, long lowest, long highest)
        {
            if (value < lowest || value > highest)
            {
                throw invocation.Fail($"This is not a real moment: its {name} is {value}.");
            }
        }

        Require(Year, Field(0), 1, 9999);
        Require(Month, Field(1), 1, 12);
        Require(Day, Field(2), 1, DateTime.DaysInMonth((int)Field(0), (int)Field(1)));
        Require(Hour, Field(3), 0, 23);
        Require(Minute, Field(4), 0, 59);
        Require(Second, Field(5), 0, 59);
        Require(Millisecond, Field(6), 0, 999);
        Require("offset_minutes", Field(7), -MostOffsetMinutes, MostOffsetMinutes);
        try
        {
            return new DateTimeOffset(
                (int)Field(0), (int)Field(1), (int)Field(2), (int)Field(3), (int)Field(4), (int)Field(5), (int)Field(6),
                TimeSpan.FromMinutes(Field(7)));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw invocation.Fail("This is not a real moment: in UTC it falls outside the years 1 to 9999.");
        }
    }

    private static string PartOf(DateTimeOffset time, string part) => part switch
    {
        Year => time.Year.ToString("D4", CultureInfo.InvariantCulture),
        Month => time.Month.ToString("D2", CultureInfo.InvariantCulture),
        Day => time.Day.ToString("D2", CultureInfo.InvariantCulture),
        Hour => time.Hour.ToString("D2", CultureInfo.InvariantCulture),
        Minute => time.Minute.ToString("D2", CultureInfo.InvariantCulture),
        Second => time.Second.ToString("D2", CultureInfo.InvariantCulture),
        Millisecond => time.Millisecond.ToString("D3", CultureInfo.InvariantCulture),
        Offset => OffsetText(time.Offset),
        MonthName => MonthNames[time.Month - 1],
        _ => WeekdayNames[(int)time.DayOfWeek],
    };

    private static string OffsetText(TimeSpan offset)
    {
        var minutes = (int)Math.Abs(offset.TotalMinutes);
        return string.Create(CultureInfo.InvariantCulture, $"{(offset < TimeSpan.Zero ? '-' : '+')}{minutes / 60:D2}:{minutes % 60:D2}");
    }

    private static int? ReadNumber(string text, ref int position, int width)
    {
        var start = position;
        var number = 0;
        while (position < text.Length && position - start < width && char.IsAsciiDigit(text[position]))
        {
            number = number * 10 + (text[position] - '0');
            position++;
        }

        return position == start ? null : number;
    }

    private static TimeSpan? ReadOffset(string text, ref int position)
    {
        if (position < text.Length && text[position] == 'Z')
        {
            position++;
            return TimeSpan.Zero;
        }

        const int Length = 6;
        if (position + Length > text.Length || text[position] is not ('+' or '-') || text[position + 3] != ':')
        {
            return null;
        }

        var digits = new[] { text[position + 1], text[position + 2], text[position + 4], text[position + 5] };
        if (!digits.All(char.IsAsciiDigit))
        {
            return null;
        }

        var minutes = ((digits[0] - '0') * 10 + (digits[1] - '0')) * 60 + (digits[2] - '0') * 10 + (digits[3] - '0');
        if (minutes > MostOffsetMinutes || digits[2] > '5')
        {
            return null;
        }

        var sign = text[position] == '-' ? -1 : 1;
        position += Length;
        return TimeSpan.FromMinutes(sign * minutes);
    }

    private static List<LayoutPiece> PiecesOf(IBuiltinInvocation invocation, string layout, bool reading)
    {
        if (ProblemIn(layout, reading) is { } problem)
        {
            throw invocation.Fail(problem);
        }

        return Split(layout, out _);
    }

    private static string? ProblemIn(string layout, bool reading)
    {
        var pieces = Split(layout, out var broken);
        if (broken)
        {
            return $"The time layout \"{layout}\" has an unmatched brace; write {{{{ and }}}} for literal braces.";
        }

        var parts = pieces.Where(piece => piece.IsPart).Select(piece => piece.Text).ToList();
        if (parts.FirstOrDefault(part => !PartNames.Contains(part)) is { } unknown)
        {
            return $"There is no part {{{unknown}}} in a time layout; the parts are {string.Join(", ", PartNames.SkipLast(1))} and {PartNames[^1]}.";
        }

        if (!reading)
        {
            return null;
        }

        if (parts.FirstOrDefault(part => part is MonthName or Weekday) is { } writeOnly)
        {
            return $"The part {{{writeOnly}}} can only be written, so it cannot be used by 'parse_moment'.";
        }

        if (parts.GroupBy(part => part).FirstOrDefault(group => group.Count() > 1) is { } repeated)
        {
            return $"The part {{{repeated.Key}}} appears twice in the time layout \"{layout}\".";
        }

        return parts.Contains(Year) ? null : $"The time layout \"{layout}\" needs {{{Year}}} to be read.";
    }

    private static List<LayoutPiece> Split(string layout, out bool broken)
    {
        var pieces = new List<LayoutPiece>();
        var literal = new StringBuilder();
        broken = false;
        var index = 0;
        while (index < layout.Length)
        {
            var character = layout[index];
            if (character is '{' or '}' && index + 1 < layout.Length && layout[index + 1] == character)
            {
                literal.Append(character);
                index += 2;
            }
            else if (character == '}')
            {
                broken = true;
                return pieces;
            }
            else if (character == '{')
            {
                var close = layout.IndexOf('}', index);
                if (close < 0)
                {
                    broken = true;
                    return pieces;
                }

                if (literal.Length > 0)
                {
                    pieces.Add(new LayoutPiece(literal.ToString(), IsPart: false));
                    literal.Clear();
                }

                pieces.Add(new LayoutPiece(layout[(index + 1)..close], IsPart: true));
                index = close + 1;
            }
            else
            {
                literal.Append(character);
                index++;
            }
        }

        if (literal.Length > 0)
        {
            pieces.Add(new LayoutPiece(literal.ToString(), IsPart: false));
        }

        return pieces;
    }
}
