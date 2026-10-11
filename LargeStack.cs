using System.Runtime.ExceptionServices;

namespace Pyrop;

public static class LargeStack
{
    private const int StackSize = 256 * 1024 * 1024;

    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = work();
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
            },
            StackSize);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
