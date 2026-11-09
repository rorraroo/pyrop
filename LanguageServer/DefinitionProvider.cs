using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.LanguageServer;

public sealed record DefinitionTarget(string? Path, int Line, int Start, int Length);

public static class DefinitionProvider
{
    public static DefinitionTarget? Find(AnalyzedSource source, int line, int column)
    {
        if (SourceWords.WordAt(source.Tokens, line, column) is not { } index)
        {
            return null;
        }

        var tokens = source.Tokens;
        var names = source.Names;
        var token = tokens[index];
        var previous = index > 0 ? tokens[index - 1] : null;
        if (previous is not null && (previous.IsWord(Keywords.Use) || previous.IsWord(Keywords.From)))
        {
            return ModuleFile(names, token.Text);
        }

        if (previous is { Kind: TokenKind.Word } && index >= 2 && tokens[index - 2].IsWord(Keywords.From))
        {
            return names.Uses.TryGetValue(previous.Text, out var use) ? OwnDeclaration(use.Target.Names, token.Text) : null;
        }

        var scopes = new ScopeResolver(source);
        if (previous is not null && (previous.IsWord(Keywords.The) || previous.IsWord(Keywords.With)))
        {
            return scopes.FieldAt(token);
        }

        if (SourceWords.RecordsOf(names).SelectMany(record => record.Fields).FirstOrDefault(field => SameToken(field.Position, token.Position)) is { } declared)
        {
            return At(declared.Position, declared.Name);
        }

        if (Keywords.IsReserved(token.Text))
        {
            return null;
        }

        return names.FindCallable(token.Text) is { } function ? At(function.Position, function.Name)
            : names.FindType(token.Text) is { } type ? At(type.Position, type.Name)
            : names.FindVariant(token.Text) is { } variant ? At(variant.Position, variant.Name)
            : scopes.VariableAt(token);
    }

    private static DefinitionTarget? ModuleFile(ModuleNamespace names, string module) =>
        names.Uses.TryGetValue(module, out var use) && use.Target.Module.FullPath is { } path ? new DefinitionTarget(path, 0, 0, 0) : null;

    private static DefinitionTarget? OwnDeclaration(ModuleNamespace target, string name) =>
        target.Records.TryGetValue(name, out var record) ? At(record.Position, name)
        : target.Choices.TryGetValue(name, out var choice) ? At(choice.Position, name)
        : target.OwnVariant(name) is { } variant ? At(variant.Position, name)
        : target.Functions.Find(name) is { } function ? At(function.Position, name)
        : null;

    private static DefinitionTarget? At(SourcePosition position, string name) =>
        position.Line < 1 ? null : new DefinitionTarget(position.Source, position.Line - 1, position.Column - 1, name.Length);

    private static bool SameToken(SourcePosition first, SourcePosition second) =>
        first.Line == second.Line && first.Column == second.Column && first.Source == second.Source;

    private sealed class Scope(Scope? parent)
    {
        private readonly Dictionary<string, SourcePosition> _names = [];

        public void Declare(string name, SourcePosition position) => _names[name] = position;

        public SourcePosition? Find(string name) => _names.TryGetValue(name, out var position) ? position : parent?.Find(name);
    }

    private sealed class ScopeResolver
    {
        private readonly AnalyzedSource _source;
        private readonly Dictionary<SourcePosition, int> _tokenAt = [];
        private readonly Dictionary<SourcePosition, SourcePosition> _definitions = [];
        private readonly Dictionary<SourcePosition, Expression> _fieldUses = [];
        private readonly Dictionary<SourcePosition, RecordField> _labels = [];
        private readonly Scope _globals = new(null);

        public ScopeResolver(AnalyzedSource source)
        {
            _source = source;
            for (var index = 0; index < source.Tokens.Count; index++)
            {
                _tokenAt[Key(source.Tokens[index].Position)] = index;
            }

            foreach (var global in source.Statements.OfType<DefStatement>())
            {
                _globals.Declare(global.Name, source.Tokens[TokenBefore(global.Value.Position)].Position);
            }

            foreach (var statement in source.Statements)
            {
                Visit(statement, _globals);
            }
        }

        public DefinitionTarget? VariableAt(Token token) =>
            _definitions.TryGetValue(Key(token.Position), out var definition) ? At(definition, token.Text) : null;

        public DefinitionTarget? FieldAt(Token token)
        {
            if (_labels.TryGetValue(Key(token.Position), out var label))
            {
                return At(label.Position, label.Name);
            }

            if (_fieldUses.TryGetValue(Key(token.Position), out var use)
                && _source.Facts.Types.GetValueOrDefault(use is GetFieldExpression read ? read.Record : ((WithFieldExpression)use).Record) is RecordType record
                && record.FindField(token.Text) is { } field)
            {
                return At(field.Position, field.Name);
            }

            var candidates = SourceWords.RecordsOf(_source.Names).SelectMany(candidate => candidate.Fields).Where(candidate => candidate.Name == token.Text).ToList();
            return candidates.Count == 1 ? At(candidates[0].Position, token.Text) : null;
        }

        private void Visit(Statement statement, Scope scope)
        {
            switch (statement)
            {
                case DefStatement def:
                    Visit(def.Value, scope);
                    Declare(scope, TokenBefore(def.Value.Position), def.Name);
                    break;
                case SetStatement set:
                    Visit(set.Value, scope);
                    Use(_source.Tokens[TokenAfter(set.Position)].Position, set.Name, scope);
                    break;
                case IfStatement conditional:
                    Visit(conditional.Condition, scope);
                    Visit(conditional.Then, scope);
                    VisitOptional(conditional.Else, scope);
                    break;
                case IfSomeStatement unwrap:
                    Visit(unwrap.Option, scope);
                    var unwrapped = new Scope(scope);
                    Declare(unwrapped, TokenBefore(unwrap.Option.Position), unwrap.Name);
                    Visit(unwrap.Then, unwrapped);
                    VisitOptional(unwrap.Else, scope);
                    break;
                case MatchStatement match:
                    Visit(match.Value, scope);
                    foreach (var branch in match.Branches)
                    {
                        var bound = new Scope(scope);
                        var first = _tokenAt[Key(branch.Position)] + (branch.Module is null ? 2 : 4);
                        for (var index = 0; index < branch.Names.Count; index++)
                        {
                            Declare(bound, first + index, branch.Names[index]);
                        }

                        Visit(branch.Body, bound);
                    }

                    break;
                case WhileStatement loop:
                    Visit(loop.Condition, scope);
                    Visit(loop.Body, scope);
                    break;
                case ForStatement loop:
                    Visit(loop.List, scope);
                    var iteration = new Scope(scope);
                    Declare(iteration, TokenBefore(loop.List.Position) - 1, loop.Variable);
                    Visit(loop.Body, iteration);
                    break;
                case FunctionStatement function:
                    var parameters = new Scope(_globals);
                    foreach (var parameter in function.Signature.Parameters)
                    {
                        parameters.Declare(parameter.Name, parameter.Position);
                        _definitions[Key(parameter.Position)] = parameter.Position;
                    }

                    Visit(function.Body, parameters);
                    break;
                case ReturnStatement { Value: { } value }:
                    Visit(value, scope);
                    break;
                case WriteStatement write:
                    Visit(write.Value, scope);
                    break;
                case ExitStatement exit:
                    Visit(exit.Code, scope);
                    break;
                case StyledStatement styled:
                    Visit(styled.Style, scope);
                    Visit(styled.Body, scope);
                    break;
                case BlockStatement block:
                    var inner = new Scope(scope);
                    foreach (var nested in block.Statements)
                    {
                        Visit(nested, inner);
                    }

                    break;
                case CallStatement call:
                    Visit(call.Call, scope);
                    break;
            }
        }

        private void VisitOptional(Statement? statement, Scope scope)
        {
            if (statement is not null)
            {
                Visit(statement, scope);
            }
        }

        private void Visit(Expression expression, Scope scope)
        {
            switch (expression)
            {
                case VariableExpression variable:
                    Use(variable.Position, variable.Name, scope);
                    break;
                case GetFieldExpression read:
                    _fieldUses[Key(read.FieldPosition)] = read;
                    break;
                case WithFieldExpression change:
                    _fieldUses[Key(change.FieldPosition)] = change;
                    break;
                case ConstructExpression { Labels: { } labels } construct:
                    foreach (var used in labels)
                    {
                        _labels[Key(used.Position)] = construct.Record.Fields[used.FieldIndex];
                    }

                    break;
            }

            foreach (var child in SyntaxTraversal.Children(expression))
            {
                Visit(child, scope);
            }
        }

        private void Declare(Scope scope, int index, string name)
        {
            if (index >= 0 && index < _source.Tokens.Count && _source.Tokens[index].IsWord(name))
            {
                var position = _source.Tokens[index].Position;
                scope.Declare(name, position);
                _definitions[Key(position)] = position;
            }
        }

        private void Use(SourcePosition position, string name, Scope scope)
        {
            if (scope.Find(name) is { } definition)
            {
                _definitions[Key(position)] = definition;
            }
        }

        private int TokenBefore(SourcePosition position) => _tokenAt[Key(position)] - 1;

        private int TokenAfter(SourcePosition position) => _tokenAt[Key(position)] + 1;

        private static SourcePosition Key(SourcePosition position) => position with { Source = null };
    }
}
