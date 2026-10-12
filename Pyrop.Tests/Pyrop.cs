using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

internal sealed class StringOutput : IProgramOutput
{
    private readonly StringBuilder _text = new();

    public void Write(string text, OutputStyle style) =>
        _text.Append(style == OutputStyle.None ? text : $"<{OutputStyles.NameOf(style)}>{text}</{OutputStyles.NameOf(style)}>");

    public void WritePrompt(string prompt, string? hint, OutputStyle style)
    {
        Write(prompt, style);
        Write(hint ?? "", OutputStyle.None);
    }

    public void LineRead()
    {
    }

    public override string ToString() => _text.ToString();
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

    public static PyropError Failure(string source, string input = "", HostContext? host = null)
    {
        var exception = Assert.Throws<PyropException>(() => Program(source, input, host));
        return Assert.Single(exception.Errors);
    }

    public static IReadOnlyList<PyropError> Failures(string source) =>
        Assert.Throws<PyropException>(() => Program(source)).Errors;
}
