namespace Pyrop.Runtime;

public enum OutputStyle
{
    None,
    Emphasis,
    Muted,
    Success,
    Warning,
    Danger,
    Info,
}

public static class OutputStyles
{
    private static readonly Dictionary<string, OutputStyle> ByName = Enum.GetValues<OutputStyle>()
        .Where(style => style != OutputStyle.None)
        .ToDictionary(NameOf);

    public static IEnumerable<string> Names => ByName.Keys;

    public static string NameOf(OutputStyle style) => style.ToString().ToLowerInvariant();

    public static OutputStyle? Find(string name) => ByName.TryGetValue(name, out var style) ? style : null;

    public static string UnknownMessage(string name) =>
        $"Unknown style \"{name}\"; the styles are {string.Join(", ", Names)}.";
}
