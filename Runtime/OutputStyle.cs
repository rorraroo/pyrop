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
    Black,
    Red,
    Green,
    Yellow,
    Blue,
    Magenta,
    Cyan,
    White,
    Gray,
    BrightRed,
    BrightGreen,
    BrightYellow,
    BrightBlue,
    BrightMagenta,
    BrightCyan,
    BrightWhite,
}

public static class OutputStyles
{
    private static readonly (string Name, OutputStyle Style, string Code)[] StyleTable =
    [
        ("emphasis", OutputStyle.Emphasis, "1"),
        ("muted", OutputStyle.Muted, "90"),
        ("success", OutputStyle.Success, "32"),
        ("warning", OutputStyle.Warning, "33"),
        ("danger", OutputStyle.Danger, "31"),
        ("info", OutputStyle.Info, "36"),
    ];

    private static readonly (string Name, OutputStyle Style, string Code)[] ColorTable =
    [
        ("black", OutputStyle.Black, "30"),
        ("red", OutputStyle.Red, "31"),
        ("green", OutputStyle.Green, "32"),
        ("yellow", OutputStyle.Yellow, "33"),
        ("blue", OutputStyle.Blue, "34"),
        ("magenta", OutputStyle.Magenta, "35"),
        ("cyan", OutputStyle.Cyan, "36"),
        ("white", OutputStyle.White, "37"),
        ("gray", OutputStyle.Gray, "90"),
        ("bright_red", OutputStyle.BrightRed, "91"),
        ("bright_green", OutputStyle.BrightGreen, "92"),
        ("bright_yellow", OutputStyle.BrightYellow, "93"),
        ("bright_blue", OutputStyle.BrightBlue, "94"),
        ("bright_magenta", OutputStyle.BrightMagenta, "95"),
        ("bright_cyan", OutputStyle.BrightCyan, "96"),
        ("bright_white", OutputStyle.BrightWhite, "97"),
    ];

    private static readonly Dictionary<string, OutputStyle> StylesByName = StyleTable.ToDictionary(entry => entry.Name, entry => entry.Style);

    private static readonly Dictionary<string, OutputStyle> ColorsByName = ColorTable.ToDictionary(entry => entry.Name, entry => entry.Style);

    private static readonly Dictionary<OutputStyle, string> Codes = StyleTable.Concat(ColorTable).ToDictionary(entry => entry.Style, entry => entry.Code);

    public static IEnumerable<string> Names => StyleTable.Select(entry => entry.Name);

    public static IEnumerable<string> ColorNames => ColorTable.Select(entry => entry.Name);

    public static OutputStyle? Find(string name) => StylesByName.TryGetValue(name, out var style) ? style : null;

    public static OutputStyle? FindColor(string name) => ColorsByName.TryGetValue(name, out var color) ? color : null;

    public static string NameOf(OutputStyle style) =>
        StyleTable.Concat(ColorTable).Single(entry => entry.Style == style).Name;

    public static string? AnsiCode(OutputStyle style) => Codes.GetValueOrDefault(style);

    public static string UnknownMessage(string name) =>
        $"Unknown style \"{name}\"; the styles are {string.Join(", ", Names)}.";

    public static string UnknownColorMessage(string name) => name == "grey"
        ? $"Unknown color \"grey\"; it is spelled \"gray\"."
        : $"Unknown color \"{name}\"; the colors are {string.Join(", ", ColorNames)}.";

    public static bool IsColorKey(string key) => TextOperations.LengthOf(key) == 1 && !key.Any(char.IsControl);

    public static string ColorKeyMessage(string key) =>
        $"A key of the color map of 'colored_grid' must be one character that is not a control character, found {TextForm.Quote(key)}.";
}
