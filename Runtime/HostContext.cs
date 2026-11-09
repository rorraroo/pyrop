namespace Pyrop.Runtime;

public sealed record HostContext(IReadOnlyList<string> Arguments, Func<string, string?> ReadEnvironment)
{
    public static HostContext Isolated { get; } = new([], _ => null);

    public static HostContext ForProcess(IReadOnlyList<string> arguments) =>
        new(arguments, Environment.GetEnvironmentVariable) { Commands = ProcessCommandRunner.Instance };

    public BuiltinLibraries Libraries { get; init; } = BuiltinLibraries.Standard;

    public ICommandRunner Commands { get; init; } = NoCommandRunner.Instance;

    public IClock Clock { get; init; } = SystemClock.Instance;
}
