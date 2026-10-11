using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Syntax;

namespace Pyrop.Tests;

public class ParserTests
{
    private static SyntaxTree Parse(string source)
    {
        var tokens = Lexer.Tokenize(source);
        return Parser.Parse(tokens, FunctionTable.Empty.With(SignatureScanner.Scan(tokens, FunctionTable.Empty)));
    }

    [Fact]
    public void ArityDeterminesNesting()
    {
        var set = Assert.IsType<SetStatement>(Assert.Single(Parse("set product multiply add 1 2 subtract 10 4").Statements));

        var multiply = Assert.IsType<BuiltinCallExpression>(set.Value);
        Assert.Equal("multiply", multiply.Operation.Name);
        Assert.Equal(["add", "subtract"], multiply.Arguments.Cast<BuiltinCallExpression>().Select(call => call.Operation.Name));
    }

    [Fact]
    public void ElseBindsToNearestIf()
    {
        var outer = Assert.IsType<IfStatement>(Assert.Single(Parse("if true if false writeline 1 else writeline 2").Statements));

        Assert.Null(outer.Else);
        Assert.NotNull(Assert.IsType<IfStatement>(outer.Then).Else);
    }

    [Fact]
    public void FunctionsCanBeCalledBeforeTheirDefinition()
    {
        var tree = Parse("writeline twice 2\nfunction int twice int x return add x x");

        Assert.IsType<FunctionCallExpression>(Assert.IsType<WriteStatement>(tree.Statements[0]).Value);
    }

    [Fact]
    public void FunctionParametersEndAtFirstNonType()
    {
        var function = Assert.IsType<FunctionStatement>(Assert.Single(Parse("function string join string a string b return add a b").Statements));

        Assert.Equal(["a", "b"], function.Signature.Parameters.Select(parameter => parameter.Name));
        Assert.IsType<ReturnStatement>(function.Body);
    }

    [Theory]
    [InlineData("writeline add 1 2 3", "cannot stand alone")]
    [InlineData("if true def int x 1", "cannot be the body")]
    [InlineData("def int x 1\ndef int x 2", "already declared")]
    [InlineData("function int f int x block\ndef int x 2\nreturn 1\nend", "shadow the parameter")]
    [InlineData("def int x\nset x 1", "needs an initial value")]
    [InlineData("block def int x end", "needs an initial value")]
    [InlineData("function int f int x int x return x", "declared twice")]
    [InlineData("break", "only allowed inside a while")]
    [InlineData("while true function void f writeline 1", "top level")]
    [InlineData("return 1", "only allowed inside a function")]
    [InlineData("function void f return", "not allowed in void function 'f'")]
    [InlineData("function void f block writeline 1 if true return end", "not allowed in void function 'f'")]
    [InlineData("function void v writeline 1\nwriteline v", "returns void")]
    [InlineData("def int add", "reserved word")]
    [InlineData("function void f writeline 1\ndef int f", "is a function")]
    [InlineData("function void f writeline 1\nfunction int f return 1", "already defined")]
    [InlineData("def void x", "cannot have type void")]
    [InlineData("end", "without a matching")]
    public void RejectsInvalidPrograms(string source, string message)
    {
        var exception = Assert.Throws<PyropException>(() => Parse(source));

        Assert.Equal(ErrorStage.Parse, exception.Errors[0].Stage);
        Assert.Contains(message, exception.Errors[0].Message);
        Assert.False(exception.IsIncompleteInput);
    }

    [Theory]
    [InlineData("block writeline 1")]
    [InlineData("writeline add 1")]
    [InlineData("function int f int x")]
    [InlineData("def int x")]
    public void FlagsTruncatedInputAsIncomplete(string source)
    {
        Assert.True(Assert.Throws<PyropException>(() => Parse(source)).IsIncompleteInput);
    }
}
