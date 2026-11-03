namespace Pyrop.Runtime;

public static class Keys
{
    public static string Read(IBuiltinInvocation invocation)
    {
        if (!invocation.Input.HasPendingLine && invocation.Output.TryReadKey(null, out var key) && key is not null)
        {
            return key;
        }

        var line = invocation.Input.ReadLine() ?? throw invocation.Fail("'read_key' reached the end of input.");
        invocation.Output.LineRead();
        return line;
    }

    public static Value ReadWithin(IBuiltinInvocation invocation, long milliseconds)
    {
        if (milliseconds is < 0 or > int.MaxValue)
        {
            throw invocation.Fail(
                $"'read_key_within' needs a time from 0 to {int.MaxValue} milliseconds, found {milliseconds}.");
        }

        var none = OptionValue.None(PyropTypes.OptionOf(PyropType.String));
        if (!invocation.Input.HasPendingLine && invocation.Output.TryReadKey((int)milliseconds, out var key))
        {
            return key is null ? none : OptionValue.Some(new StringValue(key));
        }

        if (invocation.Input.HasEnded())
        {
            Thread.Sleep((int)milliseconds);
            return none;
        }

        var line = invocation.Input.ReadLine()!;
        invocation.Output.LineRead();
        return line.Length == 0 ? none : OptionValue.Some(new StringValue(line));
    }
}
