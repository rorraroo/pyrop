using Pyrop.Runtime;
using Pyrop.Syntax;
using Spectre.Console;

namespace Pyrop.Cli;

public static class TreeCommand
{
    public static int Run(IReadOnlyList<string> arguments)
    {
        if (arguments is not [var script] || script.StartsWith('-'))
        {
            Terminal.Errors.MarkupLine("Usage: [bold]pyrop --tree[/] [grey]<file.prp>[/]");
            return 2;
        }

        if (ScriptRunner.ReadScript(script) is not { } source)
        {
            return 1;
        }

        var session = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true));
        var inspected = session.Inspect(source, script);
        var output = Terminal.CreateProgramOutput();
        if (inspected.Tree is { } tree)
        {
            output.Write(TreePrinter.Print(tree), OutputStyle.None);
        }

        if (inspected.Error is not { } error)
        {
            return 0;
        }

        Terminal.SeparateErrorsFrom(output);
        ErrorRenderer.Render(error, script, session.SourceText);
        return 1;
    }
}
