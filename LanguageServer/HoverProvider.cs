using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.LanguageServer;

public sealed record HoverResult(int Line, int Start, int Length, string Markdown);

public static class HoverProvider
{
    public static HoverResult? Describe(AnalyzedSource source, int line, int column)
    {
        var tokens = source.Tokens;
        var index = -1;
        for (var candidate = 0; candidate < tokens.Count; candidate++)
        {
            var token = tokens[candidate];
            if (token.Kind == TokenKind.Word && token.Position.Line == line
                && token.Position.Column <= column && column < token.Position.Column + token.Text.Length)
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            return null;
        }

        var hovered = tokens[index];
        var markdown = new Explainer(source).Explain(index);
        return markdown is null ? null : new HoverResult(line - 1, hovered.Position.Column - 1, hovered.Text.Length, markdown);
    }

    private sealed record Named(PyropType? Type, string Name);

    private sealed class Explainer
    {
        private readonly AnalyzedSource _source;
        private readonly ModuleNamespace _names;
        private readonly Dictionary<SourcePosition, int> _tokenAt = [];
        private readonly Dictionary<SourcePosition, Expression> _expressions = [];
        private readonly Dictionary<SourcePosition, Expression> _fields = [];
        private readonly Dictionary<SourcePosition, Statement> _statements = [];
        private readonly Dictionary<SourcePosition, Named> _declared = [];

        public Explainer(AnalyzedSource source)
        {
            _source = source;
            _names = source.Names;
            for (var index = 0; index < source.Tokens.Count; index++)
            {
                _tokenAt[Key(source.Tokens[index].Position)] = index;
            }

            foreach (var statement in source.Statements)
            {
                Visit(statement);
            }
        }

        public string? Explain(int index)
        {
            var tokens = _source.Tokens;
            var token = tokens[index];
            var at = Key(token.Position);
            if (QualifiedHead(index) is { } qualified)
            {
                return ExplainExpression(qualified);
            }

            if (_expressions.TryGetValue(at, out var expression))
            {
                return ExplainExpression(expression);
            }

            if (_fields.TryGetValue(at, out var fieldUse))
            {
                return ExplainFieldUse(fieldUse);
            }

            if (_declared.TryGetValue(at, out var named))
            {
                return Code(Declared(named.Type, named.Name));
            }

            if (_statements.TryGetValue(at, out var statement)
                || (token.IsWord(Keywords.Export) && index + 1 < tokens.Count && _statements.TryGetValue(Key(tokens[index + 1].Position), out statement)))
            {
                return statement is BlockStatement ? null : Code(TreePrinter.Heading(statement, _names));
            }

            return ExplainName(token, index > 0 && (tokens[index - 1].IsWord(Keywords.Use) || tokens[index - 1].IsWord(Keywords.From)));
        }

        private Expression? QualifiedHead(int index)
        {
            var tokens = _source.Tokens;
            foreach (var back in (int[])[1, 2])
            {
                if (index - back >= 0 && tokens[index - back].IsWord(Keywords.From)
                    && _expressions.TryGetValue(Key(tokens[index - back].Position), out var expression))
                {
                    return expression;
                }
            }

            return null;
        }

        private string? ExplainExpression(Expression expression)
        {
            switch (expression)
            {
                case LiteralExpression:
                    return null;
                case VariableExpression variable:
                    return Code(Declared(TypeOf(expression), variable.Name));
            }

            var parts = new List<string> { Code(TreePrinter.Expression(expression, _names)) };
            if (TypeOf(expression) is { } type && type != PyropType.Void)
            {
                parts.Add($"Gives `{TreePrinter.Type(type, _names)}`.");
            }

            switch (expression)
            {
                case BuiltinCallExpression call:
                    parts.Add(Overloads(call.Operation, _source.Facts.Overloads.GetValueOrDefault(call)));
                    break;
                case FunctionCallExpression call:
                    parts.Add(ExplainFunction(call.Function));
                    break;
                case ConstructExpression construct:
                    parts.Add(Code(TreePrinter.Declaration(construct.Record, _names)));
                    break;
                case VariantExpression variant:
                    parts.Add(Code(VariantLine(variant.Variant)));
                    break;
            }

            return string.Join("\n\n", parts);
        }

        private string? ExplainFieldUse(Expression use)
        {
            var (field, record) = use switch
            {
                GetFieldExpression read => (read.Field, read.Record),
                WithFieldExpression change => (change.Field, change.Record),
                _ => throw new InvalidOperationException($"Unknown field use {use.GetType().Name}."),
            };
            if (TypeOf(record) is not RecordType recordType || recordType.FindField(field) is not { } found)
            {
                return Code(field);
            }

            return ExplainField(found, recordType);
        }

        private string ExplainField(RecordField field, RecordType record) =>
            $"{Code(Declared(field.Type, field.Name))}\n\nA field of `{TreePrinter.Type(record, _names)}`.";

        private string? ExplainName(Token token, bool moduleWord)
        {
            var text = token.Text;
            if (_names.Records.Values.SelectMany(record => record.Fields.Select(field => (record, field)))
                    .FirstOrDefault(pair => Key(pair.field.Position) == Key(token.Position)) is { record: not null } declaredField)
            {
                return ExplainField(declaredField.field, declaredField.record);
            }

            if (moduleWord && _names.Uses.TryGetValue(text, out var use))
            {
                var statement = _source.Statements.OfType<UseStatement>().FirstOrDefault(candidate => candidate.Name == text);
                return Code(statement is not null
                    ? TreePrinter.Heading(statement, _names)
                    : use.Target.IsLibrary ? $"{Keywords.Use} {text} {Keywords.Library}" : $"{Keywords.Use} {text} {TextForm.Quote(use.Target.Module.DisplayName)}");
            }

            if (_names.FindOperation(text) is { } operation)
            {
                return Overloads(operation, null);
            }

            if (_names.FindCallable(text) is { } function)
            {
                return ExplainFunction(function);
            }

            if (_names.FindType(text) is { } type)
            {
                return Code(TreePrinter.Declaration(type, _names));
            }

            if (_names.FindVariant(text) is { } variant)
            {
                return Code(VariantLine(variant));
            }

            return null;
        }

        private string ExplainFunction(FunctionSignature function)
        {
            var header = Code(TreePrinter.Signature(function, _names));
            return function.Module == _names.Module ? header : $"{header}\n\nDeclared in `{function.Module.DisplayName}`.";
        }

        private string Overloads(BuiltinOperation operation, BuiltinOverload? chosen)
        {
            var ordered = chosen is null ? operation.Overloads : operation.Overloads.OrderBy(overload => overload == chosen ? 0 : 1).ToList();
            return Code(string.Join("\n", ordered.Select(overload =>
                string.Join(" ", overload.Parameters.Select(parameter => TreePrinter.Type(parameter, _names)).Prepend(operation.Name))
                + $" → {TreePrinter.Type(overload.Result, _names)}")));
        }

        private string VariantLine(ChoiceVariant variant) =>
            string.Join(" ", variant.Data.Select(type => TreePrinter.Type(type, _names)).Prepend(variant.Name))
            + $" → {TreePrinter.Type(variant.Choice, _names)}";

        private string Declared(PyropType? type, string name) => type is null ? name : $"{TreePrinter.Type(type, _names)} {name}";

        private PyropType? TypeOf(Expression expression) => _source.Facts.Types.GetValueOrDefault(expression);

        private void Visit(Statement statement)
        {
            _statements[Key(statement.Position)] = statement;
            switch (statement)
            {
                case DefStatement def:
                    Declare(TokenBefore(def.Value.Position), def.Type, def.Name);
                    Visit(def.Value);
                    break;
                case SetStatement set:
                    Declare(TokenAfter(set.Position), TypeOf(set.Value), set.Name);
                    Visit(set.Value);
                    break;
                case IfStatement conditional:
                    Visit(conditional.Condition);
                    Visit(conditional.Then);
                    VisitOptional(conditional.Else);
                    break;
                case IfSomeStatement unwrap:
                    Declare(TokenBefore(unwrap.Option.Position), unwrap.ValueType, unwrap.Name);
                    Visit(unwrap.Option);
                    Visit(unwrap.Then);
                    VisitOptional(unwrap.Else);
                    break;
                case MatchStatement match:
                    Visit(match.Value);
                    foreach (var branch in match.Branches)
                    {
                        var first = _tokenAt[Key(branch.Position)] + (branch.Module is null ? 2 : 4);
                        for (var index = 0; index < branch.Names.Count; index++)
                        {
                            Declare(first + index, branch.Variant.Data[index], branch.Names[index]);
                        }

                        Visit(branch.Body);
                    }

                    break;
                case WhileStatement loop:
                    Visit(loop.Condition);
                    Visit(loop.Body);
                    break;
                case ForStatement loop:
                    Declare(TokenBefore(loop.List.Position) - 1, loop.VariableType, loop.Variable);
                    Visit(loop.List);
                    Visit(loop.Body);
                    break;
                case FunctionStatement function:
                    foreach (var parameter in function.Signature.Parameters)
                    {
                        _declared[Key(parameter.Position)] = new Named(parameter.Type, parameter.Name);
                    }

                    Visit(function.Body);
                    break;
                case ReturnStatement { Value: { } value }:
                    Visit(value);
                    break;
                case WriteStatement write:
                    Visit(write.Value);
                    break;
                case ExitStatement exit:
                    Visit(exit.Code);
                    break;
                case StyledStatement styled:
                    Visit(styled.Style);
                    Visit(styled.Body);
                    break;
                case BlockStatement block:
                    foreach (var inner in block.Statements)
                    {
                        Visit(inner);
                    }

                    break;
                case CallStatement call:
                    _statements.Remove(Key(statement.Position));
                    Visit(call.Call);
                    break;
            }
        }

        private void VisitOptional(Statement? statement)
        {
            if (statement is not null)
            {
                Visit(statement);
            }
        }

        private void Visit(Expression expression)
        {
            _expressions.TryAdd(Key(expression.Position), expression);
            switch (expression)
            {
                case BuiltinCallExpression call:
                    VisitAll(call.Arguments);
                    break;
                case FunctionCallExpression call:
                    VisitAll(call.Arguments);
                    break;
                case ConstructExpression construct:
                    VisitAll(construct.Arguments);
                    break;
                case VariantExpression variant:
                    VisitAll(variant.Arguments);
                    break;
                case ConvertExpression convert:
                    Visit(convert.Operand);
                    break;
                case ListOfExpression list:
                    VisitAll(list.Items);
                    break;
                case MapOfExpression map:
                    foreach (var entry in map.Entries)
                    {
                        Visit(entry.Key);
                        Visit(entry.Value);
                    }

                    break;
                case GetFieldExpression read:
                    _fields[Key(read.FieldPosition)] = read;
                    Visit(read.Record);
                    break;
                case WithFieldExpression change:
                    _fields[Key(change.FieldPosition)] = change;
                    Visit(change.Record);
                    Visit(change.Value);
                    break;
                case AttemptExpression attempt:
                    Visit(attempt.Operation);
                    break;
            }
        }

        private void VisitAll(IEnumerable<Expression> expressions)
        {
            foreach (var expression in expressions)
            {
                Visit(expression);
            }
        }

        private void Declare(int index, PyropType? type, string name)
        {
            if (index >= 0 && index < _source.Tokens.Count && _source.Tokens[index].IsWord(name))
            {
                _declared[Key(_source.Tokens[index].Position)] = new Named(type, name);
            }
        }

        private int TokenBefore(SourcePosition position) => _tokenAt[Key(position)] - 1;

        private int TokenAfter(SourcePosition position) => _tokenAt[Key(position)] + 1;

        private static string Code(string text) => $"```pyrop\n{text}\n```";

        private static SourcePosition Key(SourcePosition position) => position with { Source = null };
    }
}
