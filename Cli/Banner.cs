using Spectre.Console;

namespace Pyrop.Cli;

public static class Banner
{
    private static readonly string[] Pixels =
    [
        ".......#####",
        "......##..##",
        ".....##..##..##..##..##.###...####...#####",
        "....#####...##..##..####....##..##..##..##",
        "...##......##..##..##......##..##..##..##",
        "..##.......#####..##.......####...#####",
        ".............##..................##",
        "........#####...................##",
    ];

    private static readonly Color Ember = Color.FromHex("#F28A5C");
    private static readonly Color Flame = Color.FromHex("#D9453E");
    private static readonly Color Garnet = Color.FromHex("#7A1C1C");

    private static int Width => Pixels.Max(row => row.Length);

    private static int Height => (Pixels.Length + 1) / 2;

    public static void Write(string version)
    {
        AnsiConsole.WriteLine();
        if (AnsiConsole.Profile.Width >= Width)
        {
            var banner = new Paragraph();
            for (var row = 0; row < Height; row++)
            {
                for (var column = 0; column < Width; column++)
                {
                    var (cell, style) = Cell(column, row);
                    banner.Append(cell, style);
                }

                banner.Append("\n");
            }

            AnsiConsole.Write(banner);
        }

        AnsiConsole.Write(new Rule("[grey]v" + version + "[/]").Centered().Justify(Justify.Left));
    }

    private static (string Cell, Style Style) Cell(int column, int row)
    {
        var top = Pixel(column, 2 * row);
        var bottom = Pixel(column, 2 * row + 1);

        return (top, bottom) switch
        {
            ({ } upper, { } lower) => ("▀", new Style(upper, lower)),
            ({ } upper, null) => ("▀", new Style(upper)),
            (null, { } lower) => ("▄", new Style(lower)),
            _ => (" ", Style.Plain),
        };
    }

    private static Color? Pixel(int x, int y) =>
        IsLetter(x, y) ? Shade(y) : null;

    private static bool IsLetter(int x, int y) =>
        y >= 0 && y < Pixels.Length && x >= 0 && x < Pixels[y].Length && Pixels[y][x] == '#';

    private static Color Shade(int y)
    {
        var position = (float)y / (Pixels.Length - 1);
        return position < 0.5f
            ? Ember.Blend(Flame, position * 2)
            : Flame.Blend(Garnet, (position - 0.5f) * 2);
    }
}
