using System.Text;
using Spectre.Console;

namespace Pyrop.Cli;

public static class FormatCommand
{
    public static int Run(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0 || arguments.Any(argument => argument.StartsWith('-')))
        {
            Terminal.Errors.MarkupLine("Usage: [bold]pyrop --format[/] [grey]<file.prp>…[/]");
            return 2;
        }

        var failed = false;
        foreach (var path in arguments)
        {
            failed |= !Format(path);
        }

        return failed ? 1 : 0;
    }

    private static bool Format(string path)
    {
        if (!File.Exists(path))
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] {Markup.Escape(ScriptRunner.MissingScript(path))}");
            return false;
        }

        string source;
        bool marked;
        try
        {
            var bytes = File.ReadAllBytes(path);
            marked = bytes is [0xEF, 0xBB, 0xBF, ..];
            source = Strict.GetString(bytes, marked ? 3 : 0, bytes.Length - (marked ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] '{Markup.Escape(path)}' is not UTF-8 text, so it is left unchanged.");
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] Cannot read '{Markup.Escape(path)}': {Markup.Escape(exception.Message)}");
            return false;
        }

        var session = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true));
        string formatted;
        try
        {
            formatted = session.Format(source, path);
        }
        catch (PyropException exception)
        {
            ErrorRenderer.Render(exception, path, session.SourceText);
            return false;
        }

        if (formatted == source)
        {
            return true;
        }

        try
        {
            File.WriteAllText(path, formatted, new UTF8Encoding(marked));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Terminal.Errors.MarkupLine($"[red bold]Error[/] Cannot write '{Markup.Escape(path)}': {Markup.Escape(exception.Message)}");
            return false;
        }
    }

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
