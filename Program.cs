using System.Reflection;
using System.Text;
using Pyrop.Cli;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

Repl.Version = typeof(Repl).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

return args switch
{
    [] => Repl.Run(),
    ["--version" or "-v"] => ShowVersion(),
    ["--help" or "-h"] => ShowHelp(),
    [var path, .. var arguments] when !path.StartsWith('-') => ScriptRunner.Run(path, arguments),
    _ => ShowUsage(),
};

static int ShowHelp()
{
    AnsiConsole.MarkupLine("[bold]Pyrop[/] — a bracket-free, prefix-notation, statically typed language.");
    AnsiConsole.WriteLine();
    WriteUsageLines();
    AnsiConsole.WriteLine();
    var options = new Grid().AddColumn(new GridColumn().PadLeft(2).PadRight(4)).AddColumn();
    options.AddRow("[bold]file.prp[/] [grey][[arguments]][/]", "Run a script, passing it any further arguments. Exits with the code given to exit, 0 if the script finishes, or 1 on any error.");
    options.AddRow("[grey](no arguments)[/]", "Start the interactive REPL.");
    options.AddRow("[bold]-h[/], [bold]--help[/]", "Show this help.");
    options.AddRow("[bold]-v[/], [bold]--version[/]", "Show the version.");
    AnsiConsole.Write(options);
    return 0;
}

static int ShowVersion()
{
    AnsiConsole.MarkupLine($"pyrop {Markup.Escape(Repl.Version)}");
    return 0;
}

static int ShowUsage()
{
    WriteUsageLines();
    AnsiConsole.MarkupLine("Run [bold]pyrop --help[/] for details.");
    return 2;
}

static void WriteUsageLines()
{
    AnsiConsole.MarkupLine("Usage: [bold]pyrop[/] [grey][[file.prp [[arguments]]]][/]");
    AnsiConsole.MarkupLine("       [bold]pyrop[/] [grey]--help | --version[/]");
}
