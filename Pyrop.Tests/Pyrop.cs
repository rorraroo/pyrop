using System.Text;
using Pyrop.Runtime;

namespace Pyrop.Tests;

internal sealed class StringOutput : IProgramOutput
{
    private readonly StringBuilder _text = new();

    public void Write(string text) => _text.Append(text);

    public override string ToString() => _text.ToString();
}

internal static class Run
{
    public static string Program(string source, string input = "")
    {
        var output = new StringOutput();
        new PyropSession(new StringReader(input), output).Run(source);
        return output.ToString();
    }

    public static PyropError Failure(string source, string input = "")
    {
        var exception = Assert.Throws<PyropException>(() => Program(source, input));
        return Assert.Single(exception.Errors);
    }

    public static IReadOnlyList<PyropError> Failures(string source) =>
        Assert.Throws<PyropException>(() => Program(source)).Errors;
}
