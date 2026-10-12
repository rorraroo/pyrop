using System.Text;
using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public static class TreePrinter
{
    private const string Indent = "    ";

    public static string Print(SyntaxTree tree)
    {
        var builder = new StringBuilder();
        foreach (var statement in tree.Statements)
        {
            WriteStatement(builder, statement, 0);
        }

        return builder.ToString();
    }

    private static void WriteStatement(StringBuilder builder, Statement statement, int depth)
    {
        switch (statement)
        {
            case IfStatement conditional:
                WriteIf(builder, conditional, depth, Keywords.If);
                break;
            case WhileStatement loop:
                WriteHeaded(builder, depth, $"{Keywords.While} {Operand(loop.Condition)}", loop.Body);
                break;
            case WithStyleStatement styled:
                WriteHeaded(builder, depth, $"{Keywords.WithStyle} {Operand(styled.Style)}", styled.Body);
                break;
            case FunctionStatement function:
                WriteHeaded(builder, depth, Header(function.Signature), function.Body);
                break;
            case BlockStatement block:
                WriteBlock(builder, depth, Keywords.Block, block);
                break;
            default:
                Line(builder, depth, Simple(statement));
                break;
        }
    }

    private static void WriteIf(StringBuilder builder, IfStatement conditional, int depth, string keyword)
    {
        WriteHeaded(builder, depth, $"{keyword} {Operand(conditional.Condition)}", conditional.Then);
        switch (conditional.Else)
        {
            case null:
                break;
            case IfStatement chained:
                WriteIf(builder, chained, depth, $"{Keywords.Else} {Keywords.If}");
                break;
            case var otherwise:
                WriteHeaded(builder, depth, Keywords.Else, otherwise);
                break;
        }
    }

    private static void WriteHeaded(StringBuilder builder, int depth, string header, Statement body)
    {
        if (body is BlockStatement block)
        {
            WriteBlock(builder, depth, $"{header} {Keywords.Block}", block);
            return;
        }

        Line(builder, depth, header);
        WriteStatement(builder, body, depth + 1);
    }

    private static void WriteBlock(StringBuilder builder, int depth, string header, BlockStatement block)
    {
        Line(builder, depth, header);
        foreach (var inner in block.Statements)
        {
            WriteStatement(builder, inner, depth + 1);
        }

        Line(builder, depth, Keywords.End);
    }

    private static string Simple(Statement statement) => statement switch
    {
        DefStatement def => $"{Keywords.Def} {def.Type.Name} {def.Name} {Operand(def.Value)}",
        SetStatement set => $"{Keywords.Set} {set.Name} {Operand(set.Value)}",
        ReturnStatement { Value: null } => $"{Keywords.Return} {PyropType.Void.Name}",
        ReturnStatement { Value: { } value } => $"{Keywords.Return} {Operand(value)}",
        WriteStatement write => $"{(write.AppendNewLine ? Keywords.WriteLine : Keywords.Write)} {Operand(write.Value)}",
        ExitStatement exit => $"{Keywords.Exit} {Operand(exit.Code)}",
        BreakStatement => Keywords.Break,
        ContinueStatement => Keywords.Continue,
        CallStatement call => Bare(call.Call),
        IncludeStatement include => $"{Keywords.Include} {Quote(include.Path)}",
        _ => throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}."),
    };

    private static string Header(FunctionSignature signature) =>
        string.Join(' ', [
            Keywords.Function,
            signature.ReturnType.Name,
            signature.Name,
            .. signature.Parameters.SelectMany(parameter => new[] { parameter.Type.Name, parameter.Name }),
        ]);

    private static string Operand(Expression expression) => expression switch
    {
        BuiltinCallExpression { Arguments.Count: > 0 } or FunctionCallExpression { Arguments.Count: > 0 } or ConvertExpression =>
            $"({Bare(expression)})",
        _ => Bare(expression),
    };

    private static string Bare(Expression expression) => expression switch
    {
        LiteralExpression { Value: StringValue text } => Quote(text.Value),
        LiteralExpression literal => literal.Value.ToText(),
        VariableExpression variable => variable.Name,
        BuiltinCallExpression call => Applied(call.Operation.Name, call.Arguments),
        FunctionCallExpression call => Applied(call.Function.Name, call.Arguments),
        ConvertExpression convert => $"{Keywords.Convert} {convert.Target.Name} {Operand(convert.Operand)}",
        _ => throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}."),
    };

    private static string Applied(string name, IReadOnlyList<Expression> arguments) =>
        arguments.Count == 0 ? name : $"{name} {string.Join(' ', arguments.Select(Operand))}";

    private static string Quote(string text) =>
        $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t")}\"";

    private static void Line(StringBuilder builder, int depth, string text)
    {
        for (var level = 0; level < depth; level++)
        {
            builder.Append(Indent);
        }

        builder.Append(text).Append('\n');
    }
}
