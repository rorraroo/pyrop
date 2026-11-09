using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.LanguageServer;

public enum SemanticKind
{
    Module,
    Type,
    Variant,
    Function,
    Parameter,
    Variable,
    Field,
}

[Flags]
public enum SemanticModifiers
{
    None = 0,
    BuiltIn = 1,
    Imported = 2,
}

public sealed record SemanticToken(int Line, int Start, int Length, SemanticKind Kind, SemanticModifiers Modifiers);

public static class SemanticClassifier
{
    public static readonly IReadOnlyList<string> KindNames = ["namespace", "type", "enumMember", "function", "parameter", "variable", "property"];

    public static readonly IReadOnlyList<string> ModifierNames = ["defaultLibrary", "imported"];

    internal sealed record FunctionExtent((int Line, int Column) Start, (int Line, int Column) End, FunctionSignature Signature)
    {
        public bool Contains((int Line, int Column) position) => Start.CompareTo(position) <= 0 && position.CompareTo(End) < 0;

        public bool HasParameter(string name) => Signature.Parameters.Any(parameter => parameter.Name == name);
    }

    public static IReadOnlyList<SemanticToken> Classify(AnalyzedSource source)
    {
        var names = source.Names;
        var fields = names.Records.Values
            .SelectMany(record => record.Fields)
            .Select(field => At(field.Position))
            .ToHashSet();
        var functions = FunctionExtents(source);
        var classified = new List<SemanticToken>();
        var tokens = source.Tokens;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Kind != TokenKind.Word)
            {
                continue;
            }

            if (token.IsWord(Keywords.From) && index + 2 < tokens.Count
                && tokens[index + 1].Kind == TokenKind.Word && tokens[index + 2].Kind == TokenKind.Word)
            {
                var module = tokens[index + 1];
                var name = tokens[index + 2];
                if (names.Uses.TryGetValue(module.Text, out var use))
                {
                    classified.Add(Classified(module, SemanticKind.Module, SemanticModifiers.None));
                    if (ExportedKind(use.Target.Names, name.Text) is { } exported)
                    {
                        var imported = index > 0 && tokens[index - 1].IsWord(Keywords.Import) ? SemanticModifiers.Imported : SemanticModifiers.None;
                        classified.Add(Classified(name, exported.Kind, exported.Modifiers | imported));
                    }
                }

                index += 2;
                continue;
            }

            if (Builtins.All.ContainsKey(token.Text))
            {
                classified.Add(Classified(token, SemanticKind.Function, SemanticModifiers.BuiltIn));
                continue;
            }

            if (Keywords.IsReserved(token.Text))
            {
                continue;
            }

            if (index > 0 && (tokens[index - 1].IsWord(Keywords.The) || tokens[index - 1].IsWord(Keywords.With))
                || fields.Contains(At(token.Position)))
            {
                classified.Add(Classified(token, SemanticKind.Field, SemanticModifiers.None));
            }
            else
            {
                var (kind, modifiers) = KindOf(names, token, functions, index > 0 && tokens[index - 1].IsWord(Keywords.Use));
                classified.Add(Classified(token, kind, modifiers));
            }
        }

        return classified;
    }

    private static (SemanticKind, SemanticModifiers) KindOf(ModuleNamespace names, Token token, IReadOnlyList<FunctionExtent> functions, bool afterUse)
    {
        var text = token.Text;
        if (afterUse && names.Uses.ContainsKey(text))
        {
            return (SemanticKind.Module, SemanticModifiers.None);
        }

        if (names.Imports.GetValueOrDefault(text)?.Operation is not null)
        {
            return (SemanticKind.Function, SemanticModifiers.BuiltIn | SemanticModifiers.Imported);
        }

        if (names.FindCallable(text) is { } function)
        {
            return (SemanticKind.Function, Origin(names, function.Module));
        }

        if (names.FindType(text) is { } type)
        {
            return (SemanticKind.Type, Origin(names, type.Module));
        }

        if (names.FindVariant(text) is { } variant)
        {
            return (SemanticKind.Variant, Origin(names, variant.Choice.Module));
        }

        var position = At(token.Position);
        var inFunction = functions.FirstOrDefault(extent => extent.Contains(position));
        return inFunction is not null && inFunction.HasParameter(text)
            ? (SemanticKind.Parameter, SemanticModifiers.None)
            : (SemanticKind.Variable, SemanticModifiers.None);
    }

    private static (SemanticKind Kind, SemanticModifiers Modifiers)? ExportedKind(ModuleNamespace target, string name) =>
        target.Records.ContainsKey(name) || target.Choices.ContainsKey(name) ? (SemanticKind.Type, SemanticModifiers.Imported)
        : target.OwnVariant(name) is not null ? (SemanticKind.Variant, SemanticModifiers.Imported)
        : target.Functions.Contains(name) ? (SemanticKind.Function, SemanticModifiers.Imported)
        : target.Operations.ContainsKey(name) ? (SemanticKind.Function, SemanticModifiers.BuiltIn)
        : null;

    private static SemanticModifiers Origin(ModuleNamespace names, Module module) =>
        module == names.Module ? SemanticModifiers.None : SemanticModifiers.Imported;

    internal static IReadOnlyList<FunctionExtent> FunctionExtents(AnalyzedSource source)
    {
        var names = source.Names;
        var own = names.Functions.Signatures
            .Where(signature => signature.Module == names.Module)
            .OrderBy(signature => At(signature.Position))
            .ToList();
        var unparsedLineStarts = source.Tokens
            .Where(token => token.Kind == TokenKind.Word && token.Position.Column == 1
                && !token.IsWord(Keywords.End) && !token.IsWord(Keywords.Else)
                && source.Gaps.Any(gap => At(token.Position).CompareTo(At(gap.From)) >= 0 && At(token.Position).CompareTo(At(gap.To)) < 0))
            .Select(token => At(token.Position));
        var starts = source.Statements
            .Where(statement => statement is not FunctionStatement)
            .Select(statement => At(statement.Position))
            .Concat(own.Select(signature => At(signature.Position)))
            .Concat(unparsedLineStarts)
            .Order()
            .ToList();
        return own
            .Select(signature =>
            {
                var start = At(signature.Position);
                var end = starts.FirstOrDefault(boundary => boundary.CompareTo(start) > 0, (int.MaxValue, int.MaxValue));
                return new FunctionExtent(start, end, signature);
            })
            .ToList();
    }

    internal static (int Line, int Column) At(SourcePosition position) => (position.Line, position.Column);

    private static SemanticToken Classified(Token token, SemanticKind kind, SemanticModifiers modifiers) =>
        new(token.Position.Line - 1, token.Position.Column - 1, token.Text.Length, kind, modifiers);
}
