using System.Text;
using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public static class TreePrinter
{
    public static string Print(SyntaxTree tree, ModuleNamespace? names = null)
    {
        var builder = new StringBuilder();
        var printer = new Printer(builder, names);
        foreach (var statement in tree.Statements)
        {
            printer.WriteStatement(statement, 0);
        }

        return builder.ToString();
    }

    private sealed class Printer(StringBuilder output, ModuleNamespace? names)
    {
        private const string Indent = "    ";

        public void WriteStatement(Statement statement, int depth)
        {
            switch (statement)
            {
                case IfStatement or IfSomeStatement:
                    WriteIf(statement, depth, "");
                    break;
                case WhileStatement loop:
                    WriteHeaded(depth, $"{Keywords.While} {Operand(loop.Condition)}", loop.Body);
                    break;
                case ForStatement loop:
                    WriteHeaded(depth, $"{Keywords.For} {TypeText(loop.VariableType)} {loop.Variable} {Keywords.In} {Operand(loop.List)}", loop.Body);
                    break;
                case WithStyleStatement styled:
                    WriteHeaded(depth, $"{Keywords.WithStyle} {Operand(styled.Style)}", styled.Body);
                    break;
                case FunctionStatement function:
                    WriteHeaded(depth, Header(function.Signature), function.Body);
                    break;
                case BlockStatement block:
                    WriteBlock(depth, Keywords.Block, block);
                    break;
                case MatchStatement match:
                    WriteMatch(match, depth);
                    break;
                default:
                    Line(depth, Simple(statement));
                    break;
            }
        }

        private void WriteIf(Statement statement, int depth, string prefix)
        {
            var (header, then, otherwise) = statement switch
            {
                IfStatement conditional => ($"{Keywords.If} {Operand(conditional.Condition)}", conditional.Then, conditional.Else),
                IfSomeStatement unwrap => (
                    $"{Keywords.IfSome} {TypeText(unwrap.ValueType)} {unwrap.Name} {Operand(unwrap.Option)}", unwrap.Then, unwrap.Else),
                _ => throw new InvalidOperationException($"Unknown conditional {statement.GetType().Name}."),
            };
            WriteHeaded(depth, prefix + header, then);
            switch (otherwise)
            {
                case null:
                    break;
                case IfStatement or IfSomeStatement:
                    WriteIf(otherwise, depth, $"{Keywords.Else} ");
                    break;
                default:
                    WriteHeaded(depth, Keywords.Else, otherwise);
                    break;
            }
        }

        private void WriteMatch(MatchStatement match, int depth)
        {
            Line(depth, $"{Keywords.Match} {Operand(match.Value)}");
            foreach (var branch in match.Branches)
            {
                WriteHeaded(depth + 1, string.Join(' ', [Keywords.When, Callee(branch.Module, branch.Variant.Name), .. branch.Names]), branch.Body);
            }

            Line(depth, Keywords.End);
        }

        private void WriteHeaded(int depth, string header, Statement body)
        {
            if (body is BlockStatement block)
            {
                WriteBlock(depth, $"{header} {Keywords.Block}", block);
                return;
            }

            Line(depth, header);
            WriteStatement(body, depth + 1);
        }

        private void WriteBlock(int depth, string header, BlockStatement block)
        {
            Line(depth, header);
            foreach (var inner in block.Statements)
            {
                WriteStatement(inner, depth + 1);
            }

            Line(depth, Keywords.End);
        }

        private string Simple(Statement statement) => statement switch
        {
            DefStatement def => $"{Keywords.Def} {TypeText(def.Type)} {def.Name} {Operand(def.Value)}",
            SetStatement set => $"{Keywords.Set} {set.Name} {Operand(set.Value)}",
            ReturnStatement { Value: null } => $"{Keywords.Return} {PyropType.Void.Name}",
            ReturnStatement { Value: { } value } => $"{Keywords.Return} {Operand(value)}",
            WriteStatement write => $"{WriteKeyword(write)} {Operand(write.Value)}",
            ExitStatement exit => $"{Keywords.Exit} {Operand(exit.Code)}",
            BreakStatement => Keywords.Break,
            ContinueStatement => Keywords.Continue,
            CallStatement call => Bare(call.Call),
            UseStatement use => $"{Keywords.Use} {use.Name} {(use.Path is null ? Keywords.Library : TextForm.Quote(use.Path))}",
            ImportStatement import => $"{Keywords.Import} {Keywords.From} {import.Module} {string.Join(' ', import.Names)} {Keywords.End}",
            RecordStatement record => RecordHeader(record.Record),
            ChoiceStatement choice => ChoiceHeader(choice.Choice),
            _ => throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}."),
        };

        private static string WriteKeyword(WriteStatement write) => (write.ToStandardError, write.AppendNewLine) switch
        {
            (false, false) => Keywords.Write,
            (false, true) => Keywords.WriteLine,
            (true, false) => Keywords.WriteError,
            (true, true) => Keywords.WriteErrorLine,
        };

        private string Header(FunctionSignature signature) =>
            string.Join(' ', [
                .. signature.IsExported ? [Keywords.Export] : Array.Empty<string>(),
                Keywords.Function,
                TypeText(signature.ReturnType),
                signature.Name,
                .. signature.Parameters.SelectMany(parameter => new[] { TypeText(parameter.Type), parameter.Name }),
            ]);

        private string RecordHeader(RecordType record) =>
            string.Join(' ', [
                .. record.IsExported ? [Keywords.Export] : Array.Empty<string>(),
                Keywords.Record,
                record.Name,
                .. record.Fields.SelectMany(field => new[] { TypeText(field.Type), field.Name }),
            ]);

        private string ChoiceHeader(ChoiceType choice) =>
            string.Join(' ', [
                .. choice.IsExported ? [Keywords.Export] : Array.Empty<string>(),
                Keywords.Choice,
                choice.Name,
                .. choice.Variants.SelectMany(variant => variant.Data.Select(TypeText).Prepend(variant.Name)),
                Keywords.End,
            ]);

        private string TypeText(PyropType type)
        {
            if (!type.DeclaredTypes.Any())
            {
                return type.Name;
            }

            var builder = new StringBuilder();
            AppendType(builder, type);
            return builder.ToString();
        }

        private void AppendType(StringBuilder builder, PyropType type)
        {
            switch (type)
            {
                case DeclaredType declared:
                    builder.Append(DeclaredText(declared));
                    break;
                case ConstructedType constructed:
                    builder.Append(constructed.Constructor.Name);
                    foreach (var argument in constructed.Arguments)
                    {
                        builder.Append(' ');
                        AppendType(builder, argument);
                    }

                    break;
                default:
                    builder.Append(type.Name);
                    break;
            }
        }

        private string DeclaredText(DeclaredType declared)
        {
            if (names is null || declared.Module == names.Module || names.FindType(declared.Name) == declared)
            {
                return declared.Name;
            }

            var use = names.Uses.Values.FirstOrDefault(candidate => candidate.Target.Module == declared.Module);
            return use is null ? declared.Name : $"{Keywords.From} {use.Name} {declared.Name}";
        }

        private string Operand(Expression expression)
        {
            var builder = new StringBuilder();
            AppendOperand(builder, expression);
            return builder.ToString();
        }

        private string Bare(Expression expression)
        {
            var builder = new StringBuilder();
            AppendBare(builder, expression);
            return builder.ToString();
        }

        private void AppendOperand(StringBuilder builder, Expression expression)
        {
            if (expression is BuiltinCallExpression { Arguments.Count: > 0 } or FunctionCallExpression { Arguments.Count: > 0 }
                or ConstructExpression { Arguments.Count: > 0 } or VariantExpression { Arguments.Count: > 0 } or ConvertExpression or ListOfExpression or MapOfExpression
                or GetFieldExpression or WithFieldExpression or AttemptExpression or NoneExpression)
            {
                builder.Append('(');
                AppendBare(builder, expression);
                builder.Append(')');
                return;
            }

            AppendBare(builder, expression);
        }

        private void AppendBare(StringBuilder builder, Expression expression)
        {
            switch (expression)
            {
                case LiteralExpression { Value: StringValue text }:
                    builder.Append(TextForm.Quote(text.Value));
                    break;
                case LiteralExpression literal:
                    builder.Append(literal.Value.ToText());
                    break;
                case VariableExpression variable:
                    builder.Append(variable.Name);
                    break;
                case BuiltinCallExpression call:
                    AppendApplied(builder, Callee(call.Module, call.Operation.Name), call.Arguments);
                    break;
                case FunctionCallExpression call:
                    AppendApplied(builder, Callee(call.Module, call.Function.Name), call.Arguments);
                    break;
                case ConstructExpression construct:
                    AppendApplied(builder, Callee(construct.Module, construct.Record.Name), construct.Arguments);
                    break;
                case ConvertExpression convert:
                    builder.Append(Keywords.Convert).Append(' ').Append(TypeText(convert.Target)).Append(' ');
                    AppendOperand(builder, convert.Operand);
                    break;
                case ListOfExpression list:
                    AppendApplied(builder, $"{Keywords.ListOf} {TypeText(list.ElementType)}", list.Items);
                    builder.Append(' ').Append(Keywords.End);
                    break;
                case MapOfExpression map:
                    AppendApplied(
                        builder,
                        $"{Keywords.MapOf} {TypeText(map.KeyType)} {TypeText(map.ValueType)}",
                        [.. map.Entries.SelectMany(entry => new[] { entry.Key, entry.Value })]);
                    builder.Append(' ').Append(Keywords.End);
                    break;
                case GetFieldExpression read:
                    AppendApplied(builder, $"{Keywords.The} {read.Field} {Keywords.Of}", [read.Record]);
                    break;
                case WithFieldExpression change:
                    builder.Append(Keywords.Copy).Append(' ');
                    AppendOperand(builder, change.Record);
                    builder.Append(' ').Append(Keywords.With).Append(' ').Append(change.Field).Append(' ');
                    AppendOperand(builder, change.Value);
                    break;
                case VariantExpression variant:
                    AppendApplied(builder, Callee(variant.Module, variant.Variant.Name), variant.Arguments);
                    break;
                case AttemptExpression attempt:
                    AppendApplied(builder, Keywords.Attempt, [attempt.Operation]);
                    break;
                case NoneExpression none:
                    builder.Append(Keywords.None).Append(' ').Append(TypeText(none.ContentType));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
            }
        }

        private static string Callee(string? module, string name) =>
            module is null ? name : $"{Keywords.From} {module} {name}";

        private void AppendApplied(StringBuilder builder, string name, IReadOnlyList<Expression> arguments)
        {
            builder.Append(name);
            foreach (var argument in arguments)
            {
                builder.Append(' ');
                AppendOperand(builder, argument);
            }
        }

        private void Line(int depth, string text)
        {
            for (var level = 0; level < depth; level++)
            {
                output.Append(Indent);
            }

            output.Append(text).Append('\n');
        }
    }
}
