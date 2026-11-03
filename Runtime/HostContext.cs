namespace Pyrop.Runtime;

public sealed record HostContext(IReadOnlyList<string> Arguments, Func<string, string?> ReadEnvironment)
{
    public static HostContext Isolated { get; } = new([], _ => null);

    public static HostContext ForProcess(IReadOnlyList<string> arguments) => new(arguments, Environment.GetEnvironmentVariable);

    public BuiltinLibraries Libraries { get; init; } = BuiltinLibraries.Standard;
}
