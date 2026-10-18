using System.Reflection;
using System.Text;
using Pyrop.Bundling;
using Pyrop.Cli;
using Pyrop.LanguageServer;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

Repl.Version = typeof(Repl).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

Bundle? bundle;
try
{
    bundle = ReadOwnBundle();
}
catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
{
    Terminal.Configure(plainRequested: false);
    Terminal.Errors.MarkupLine("[red bold]Error[/] This program is damaged: the Pyrop program inside it cannot be read.");
    return 1;
}

if (bundle is not null)
{
    Terminal.Configure(plainRequested: false);
    return ScriptRunner.RunBundle(bundle, args);
}

var plain = args is ["--plain", ..];
Terminal.Configure(plain);

return (plain ? args[1..] : args) switch
{
    [] => Repl.Run(),
    ["--version" or "-v"] => ShowVersion(),
    ["--help" or "-h"] => ShowHelp(),
    ["--build" or "-b", .. var buildArguments] => BuildCommand.Run(buildArguments),
    ["--tree" or "-t", .. var treeArguments] => TreeCommand.Run(treeArguments),
    ["--lsp"] => PyropLanguageServer.Run(Console.OpenStandardInput(), Console.OpenStandardOutput(), PyropLanguageServer.CheckDelay),
    [var path, .. var arguments] when !path.StartsWith('-') => ScriptRunner.Run(path, arguments),
    _ => ShowUsage(),
};

static Bundle? ReadOwnBundle()
{
    if (Environment.ProcessPath is not { } path)
    {
        return null;
    }

    try
    {
        return Bundle.ReadFrom(path);
    }
    catch (Exception exception) when (exception is (IOException or UnauthorizedAccessException) and not InvalidDataException)
    {
        return null;
    }
}

static int ShowHelp()
{
    AnsiConsole.MarkupLine("[bold]Pyrop[/] — a bracket-free, prefix-notation, statically typed language.");
    AnsiConsole.WriteLine();
    WriteUsageLines(AnsiConsole.Console);
    AnsiConsole.WriteLine();
    var options = new Grid().AddColumn(new GridColumn().PadLeft(2).PadRight(4)).AddColumn();
    options.AddRow("[bold]file.prp[/] [grey][[arguments]][/]", "Run a script, passing it any further arguments. Exits with the code given to exit, 0 if the script finishes, or 1 on any error.");
    options.AddRow("[bold]-b[/], [bold]--build[/] [grey]file.prp [[-o output]][/]", "Build a standalone executable containing the script and the modules it uses. Needs the standalone pyrop.");
    options.AddRow("[bold]-t[/], [bold]--tree[/] [grey]file.prp[/]", "Show the program with brackets around every call, to see how the parser grouped it. Statements before a parse error are still shown.");
    options.AddRow("[bold]--lsp[/]", "Run a language server on standard input and output, for editors to show errors while you type.");
    options.AddRow("[grey](no arguments)[/]", "Start the interactive REPL.");
    options.AddRow("[bold]--plain[/]", "Write no colours, styles or prompt decorations. Also on when output is redirected or NO_COLOR is set.");
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
    WriteUsageLines(Terminal.Errors);
    Terminal.Errors.MarkupLine("Run [bold]pyrop --help[/] for details.");
    return 2;
}

static void WriteUsageLines(IAnsiConsole console)
{
    console.MarkupLine("Usage: [bold]pyrop[/] [grey][[--plain]] [[file.prp [[arguments]]]][/]");
    console.MarkupLine("       [bold]pyrop[/] [grey]--build file.prp [[-o output]][/]");
    console.MarkupLine("       [bold]pyrop[/] [grey]--tree file.prp[/]");
    console.MarkupLine("       [bold]pyrop[/] [grey]--lsp[/]");
    console.MarkupLine("       [bold]pyrop[/] [grey]--help | --version[/]");
}
