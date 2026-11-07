using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

internal sealed class StringOutput : IProgramOutput
{
    private readonly StringBuilder _text = new();
    private readonly StringBuilder _errors = new();
    private bool _atLineStart = true;
    private bool _errorsAtLineStart = true;
    private bool _errorsWroteLast;

    public string Errors => _errors.ToString();

    public long Written { get; private set; }

    public void Write(string text, OutputStyle style)
    {
        Append(_text, text, style);
        if (text.Length > 0)
        {
            Written++;
            _atLineStart = text[^1] == '\n';
            _errorsWroteLast = false;
        }
    }

    public void WriteError(string text, OutputStyle style)
    {
        Append(_errors, text, style);
        if (text.Length > 0)
        {
            Written++;
            _errorsAtLineStart = text[^1] == '\n';
            _errorsWroteLast = true;
        }
    }

    public void WritePrompt(string prompt, string? hint, OutputStyle style)
    {
        Write(prompt, style);
        Write(hint ?? "", OutputStyle.None);
    }

    public void LineRead()
    {
        if (!_errorsWroteLast)
        {
            EnsureLineStart();
        }
        else if (!_errorsAtLineStart)
        {
            WriteError("\n", OutputStyle.None);
        }
    }

    public void EnsureLineStart()
    {
        if (!_atLineStart)
        {
            Write("\n", OutputStyle.None);
        }
    }

    public int? TryChoose(string prompt, IReadOnlyList<string> entries, OutputStyle style) => null;

    public bool TryWriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, OutputStyle style) => false;

    public override string ToString() => _text.ToString();

    private static void Append(StringBuilder builder, string text, OutputStyle style) =>
        builder.Append(style == OutputStyle.None ? text : $"<{OutputStyles.NameOf(style)}>{text}</{OutputStyles.NameOf(style)}>");
}

internal static class Run
{
    public static string Program(string source, string input = "", HostContext? host = null) =>
        Execute(source, input, host).Output;

    public static (string Output, int? ExitCode) Execute(string source, string input = "", HostContext? host = null)
    {
        var output = new StringOutput();
        var exitCode = new PyropSession(new StringReader(input), output, host).Run(source);
        return (output.ToString(), exitCode);
    }

    public static (string Output, string Errors) Streams(string source, string input = "")
    {
        var output = new StringOutput();
        new PyropSession(new StringReader(input), output).Run(source);
        return (output.ToString(), output.Errors);
    }

    public static PyropError Failure(string source, string input = "", HostContext? host = null)
    {
        var exception = Assert.Throws<PyropException>(() => Program(source, input, host));
        return Assert.Single(exception.Errors);
    }

    public static IReadOnlyList<PyropError> Failures(string source) =>
        Assert.Throws<PyropException>(() => Program(source)).Errors;
}
