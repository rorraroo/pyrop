namespace Pyrop.Runtime;

public interface IClock
{
    DateTimeOffset Now { get; }

    DateTimeOffset UtcNow { get; }

    TimeSpan OffsetAt(DateTime time);

    void Wait(int milliseconds);
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public TimeSpan OffsetAt(DateTime time) => TimeZoneInfo.Local.GetUtcOffset(time);

    public void Wait(int milliseconds) => Thread.Sleep(milliseconds);
}
