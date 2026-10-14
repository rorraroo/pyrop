using Spectre.Console;

namespace Pyrop.Cli;

public static class Banner
{
    private static readonly (string Row, string Colour)[] Rows =
    [
        ("██████╗ ██╗   ██╗██████╗  ██████╗ ██████╗ ", "#F28A5C"),
        ("██╔══██╗╚██╗ ██╔╝██╔══██╗██╔═══██╗██╔══██╗", "#E86E50"),
        ("██████╔╝ ╚████╔╝ ██████╔╝██║   ██║██████╔╝", "#DE5344"),
        ("██╔═══╝   ╚██╔╝  ██╔══██╗██║   ██║██╔═══╝ ", "#C63D37"),
        ("██║        ██║   ██║  ██║╚██████╔╝██║     ", "#A02C2A"),
        ("╚═╝        ╚═╝   ╚═╝  ╚═╝ ╚═════╝ ╚═╝     ", "#7A1C1C"),
    ];

    public static void Write(string version)
    {
        if (AnsiConsole.Profile.Width >= Rows[0].Row.Length)
        {
            foreach (var (row, colour) in Rows)
            {
                AnsiConsole.Write(new Text(row.TrimEnd(), new Style(Color.FromHex(colour))));
                AnsiConsole.WriteLine();
            }
        }

        AnsiConsole.Write(new Rule("[grey]v" + version + "[/]").Centered().Justify(Justify.Left));
    }
}
