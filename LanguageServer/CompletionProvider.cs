using Pyrop.Lexing;
using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.LanguageServer;

public sealed record CompletionEntry(string Label, int Kind, int Rank, string? Takes, string? Detail, string? Documentation);

public static class CompletionProvider
{
    private const int FunctionKind = 3;
    private const int ConstructorKind = 4;
    private const int FieldKind = 5;
    private const int VariableKind = 6;
    private const int ModuleKind = 9;
    private const int EnumKind = 13;
    private const int KeywordKind = 14;
    private const int VariantKind = 20;
    private const int StructKind = 22;
    private const int TypeKind = 25;

    private enum Place
    {
        Nothing,
        Modules,
        Exported,
        Fields,
        Types,
        ReturnTypes,
        TypesAndWords,
        Words,
    }

    public static IReadOnlyList<CompletionEntry> Complete(AnalyzedSource source, int line, int column, bool typedSpace) =>
        IsInStringOrComment(source.Text, line, column) ? [] : new Completer(source, line, column, typedSpace).Entries();

    private static bool IsInStringOrComment(string text, int line, int column)
    {
        var lines = text.Split('\n');
        if (line > lines.Length)
        {
            return false;
        }

        var written = lines[line - 1];
        var inString = false;
        for (var index = 0; index < Math.Min(column - 1, written.Length); index++)
        {
            var character = written[index];
            if (inString)
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == '"')
                {
                    inString = false;
                }
            }
            else if (character == '"')
            {
                inString = true;
            }
            else if (character == '#')
            {
                return true;
            }
        }

        return inString;
    }

    private sealed class Completer
    {
        private readonly AnalyzedSource _source;
        private readonly IReadOnlyList<Token> _tokens;
        private readonly ModuleNamespace _names;
        private readonly (int Line, int Column) _cursor;
        private readonly bool _typedSpace;
        private readonly int _at;
        private readonly string _prefix = "";
        private readonly List<CompletionEntry> _entries = [];
        private readonly HashSet<string> _labels = [];
        private string? _module;

        public Completer(AnalyzedSource source, int line, int column, bool typedSpace)
        {
            _source = source;
            _tokens = source.Tokens;
            _names = source.Names;
            _cursor = (line, column);
            _typedSpace = typedSpace;
            for (_at = 0; _at < _tokens.Count - 1; _at++)
            {
                var token = _tokens[_at];
                if (token.Kind == TokenKind.Word && token.Position.Line == line
                    && column > token.Position.Column && column <= token.Position.Column + token.Text.Length)
                {
                    _prefix = token.Text[..(column - token.Position.Column)];
                    break;
                }

                if (SemanticClassifier.At(token.Position).CompareTo(_cursor) >= 0)
                {
                    break;
                }
            }
        }

        public IReadOnlyList<CompletionEntry> Entries()
        {
            switch (PlaceAtCursor())
            {
                case Place.Modules:
                    foreach (var module in _names.Uses.Keys)
                    {
                        Add(module, ModuleKind, 0);
                    }

                    break;
                case Place.Exported:
                    AddExported();
                    break;
                case Place.Fields:
                    AddFields();
                    break;
                case Place.Types:
                    AddTypes();
                    break;
                case Place.ReturnTypes:
                    AddTypes();
                    Add(PyropType.Void.Name, TypeKind, 0);
                    break;
                case Place.TypesAndWords:
                    AddTypes();
                    if (!_typedSpace)
                    {
                        AddWords();
                    }

                    break;
                case Place.Words:
                    if (!_typedSpace)
                    {
                        AddWords();
                    }

                    break;
            }

            return _entries;
        }

        private Place PlaceAtCursor()
        {
            if (_at == 0)
            {
                return Place.Words;
            }

            var previous = _tokens[_at - 1];
            if (previous.IsWord(Keywords.From))
            {
                return Place.Modules;
            }

            if (previous.Kind == TokenKind.Word && _at >= 2 && _tokens[_at - 2].IsWord(Keywords.From))
            {
                _module = previous.Text;
                return Place.Exported;
            }

            if (ImportedModule() is { } imported)
            {
                _module = imported;
                return Place.Exported;
            }

            if (previous.IsWord(Keywords.Use) || (_at >= 2 && _tokens[_at - 2].IsWord(Keywords.Use)))
            {
                return Place.Nothing;
            }

            if (previous.IsWord(Keywords.The) || previous.IsWord(Keywords.With))
            {
                return Place.Fields;
            }

            return TypedFormPlace() ?? DeclarationPlace() ?? Place.Words;
        }

        private string? ImportedModule()
        {
            for (var index = _at - 1; index >= 0 && _tokens[index].Kind == TokenKind.Word && !Keywords.IsStatement(_tokens[index].Text); index--)
            {
                if (_tokens[index].IsWord(Keywords.From) && index > 0 && _tokens[index - 1].IsWord(Keywords.Import) && index + 1 < _at)
                {
                    return _tokens[index + 1].Text;
                }
            }

            return null;
        }

        private Place? TypedFormPlace()
        {
            for (var index = _at - 1; index >= Math.Max(0, _at - 8); index--)
            {
                var token = _tokens[index];
                if (token.IsWord(Keywords.Def) || token.IsWord(Keywords.For) || token.IsWord(Keywords.IfSome))
                {
                    var length = TypeLength(index + 1);
                    return _at <= index || _at < index + 1 + Math.Max(length, 1) ? Place.Types
                        : _at == index + 1 + length ? Place.Nothing
                        : null;
                }

                if (token.IsWord(Keywords.Convert) || token.IsWord(Keywords.None) || token.IsWord(Keywords.ListOf) || token.IsWord(Keywords.VariantsOf))
                {
                    return _at < index + 1 + Math.Max(TypeLength(index + 1), 1) ? Place.Types : null;
                }

                if (token.IsWord(Keywords.MapOf))
                {
                    var key = Math.Max(TypeLength(index + 1), 1);
                    return _at < index + 1 + key + Math.Max(TypeLength(index + 1 + key), 1) ? Place.Types : null;
                }
            }

            return null;
        }

        private Place? DeclarationPlace()
        {
            var start = _at - 1;
            while (start > 0 && _tokens[start].Kind == TokenKind.Word && !Keywords.IsStatement(_tokens[start].Text))
            {
                start--;
            }

            var keyword = _tokens[start];
            if (keyword.IsWord(Keywords.Choice))
            {
                return _at == start + 1 ? Place.Nothing : Place.Types;
            }

            if (keyword.IsWord(Keywords.Record))
            {
                return _at == start + 1 ? Place.Nothing : Slots(start + 2);
            }

            if (!keyword.IsWord(Keywords.Function))
            {
                return null;
            }

            var returned = Math.Max(TypeLength(start + 1), 1);
            if (_at < start + 1 + returned)
            {
                return Place.ReturnTypes;
            }

            return _at == start + 1 + returned ? Place.Nothing : Slots(start + 2 + returned);
        }

        private Place? Slots(int first)
        {
            var index = first;
            while (index <= _at && index < _tokens.Count)
            {
                if (_at == index)
                {
                    return Place.TypesAndWords;
                }

                if (!StartsType(index))
                {
                    return null;
                }

                var length = TypeLength(index);
                if (_at < index + length)
                {
                    return Place.Types;
                }

                if (_at == index + length)
                {
                    return Place.Nothing;
                }

                index += length + 1;
            }

            return null;
        }

        private bool StartsType(int index)
        {
            var token = _tokens[index];
            if (token.Kind != TokenKind.Word)
            {
                return false;
            }

            if (token.IsWord(Keywords.From))
            {
                return index + 2 < _tokens.Count
                    && _names.Uses.TryGetValue(_tokens[index + 1].Text, out var use)
                    && (use.Target.Names.Records.ContainsKey(_tokens[index + 2].Text) || use.Target.Names.Choices.ContainsKey(_tokens[index + 2].Text));
            }

            return PyropTypes.ValueConstructors.ContainsKey(token.Text) || _names.FindType(token.Text) is not null;
        }

        private int TypeLength(int start)
        {
            var index = start;
            var needed = 1;
            while (needed > 0 && index < _tokens.Count && _tokens[index].Kind == TokenKind.Word)
            {
                if (_tokens[index].IsWord(Keywords.From))
                {
                    index += 3;
                    needed--;
                }
                else
                {
                    needed += PyropTypes.ValueConstructors.TryGetValue(_tokens[index].Text, out var constructor) ? constructor.Arity - 1 : -1;
                    index++;
                }
            }

            return Math.Min(index, _tokens.Count) - start + needed;
        }

        private void Add(string label, int kind, int rank, string? takes = null, string? detail = null, string? documentation = null)
        {
            if (label.StartsWith(_prefix, StringComparison.Ordinal) && _labels.Add(label))
            {
                _entries.Add(new CompletionEntry(label, kind, rank, takes, detail, documentation));
            }
        }

        private static string Takes(int arity) => arity == 0 ? "takes nothing" : $"takes {arity}";

        private void AddFunction(FunctionSignature function) =>
            Add(function.Name, FunctionKind, 1, Takes(function.Arity), TreePrinter.Signature(function, _names));

        private void AddRecord(RecordType record) =>
            Add(record.Name, ConstructorKind, 2, Takes(record.Fields.Count), TreePrinter.Declaration(record, _names));

        private void AddVariant(ChoiceVariant variant) =>
            Add(variant.Name, VariantKind, 2, variant.Arity > 0 ? Takes(variant.Arity) : null, SourceWords.VariantLine(variant, _names));

        private void AddOperation(BuiltinOperation operation, string? label = null)
        {
            var signatures = SourceWords.Overloads(operation, _names);
            var first = signatures.Split('\n')[0];
            Add(label ?? operation.Name, FunctionKind, 3, Takes(operation.Arity), first, signatures == first ? null : SourceWords.Code(signatures));
        }

        private void AddExported()
        {
            if (_module is null || !_names.Uses.TryGetValue(_module, out var use))
            {
                return;
            }

            var target = use.Target.Names;
            foreach (var operation in target.Operations.Values)
            {
                AddOperation(operation);
            }

            foreach (var function in target.Functions.Signatures.Where(function => function.IsExported && function.Module == target.Module))
            {
                AddFunction(function);
            }

            foreach (var record in target.Records.Values.Where(record => record.IsExported))
            {
                AddRecord(record);
            }

            foreach (var choice in target.Choices.Values.Where(choice => choice.IsExported))
            {
                Add(choice.Name, EnumKind, 2, detail: TreePrinter.Declaration(choice, _names));
                foreach (var variant in choice.Variants)
                {
                    AddVariant(variant);
                }
            }
        }

        private void AddFields()
        {
            foreach (var group in SourceWords.RecordsOf(_names)
                .Concat(_names.Uses.Values.SelectMany(use => use.Target.Names.Records.Values.Where(record => record.IsExported)))
                .Distinct()
                .SelectMany(record => record.Fields.Select(field => (Record: record, Field: field)))
                .GroupBy(pair => pair.Field.Name))
            {
                var owners = string.Join(", ", group.Select(pair => $"{TreePrinter.Type(pair.Field.Type, _names)} in {TreePrinter.Type(pair.Record, _names)}"));
                Add(group.Key, FieldKind, 0, detail: owners);
            }
        }

        private void AddTypes()
        {
            foreach (var name in PyropTypes.ValueConstructors.Keys)
            {
                Add(name, TypeKind, 0);
            }

            foreach (var record in SourceWords.RecordsOf(_names))
            {
                Add(record.Name, StructKind, 1, detail: TreePrinter.Declaration(record, _names));
            }

            foreach (var choice in SourceWords.ChoicesOf(_names))
            {
                Add(choice.Name, EnumKind, 1, detail: TreePrinter.Declaration(choice, _names));
            }
        }

        private void AddWords()
        {
            AddVariables();
            foreach (var function in _names.Functions.Signatures)
            {
                AddFunction(function);
            }

            foreach (var import in _names.Imports.Values)
            {
                if (import.Function is { } function)
                {
                    AddFunction(function);
                }
                else if (import.Operation is { } operation)
                {
                    AddOperation(operation, import.Name);
                }
            }

            foreach (var record in SourceWords.RecordsOf(_names))
            {
                AddRecord(record);
            }

            foreach (var variant in _names.Variants.Values)
            {
                AddVariant(variant);
            }

            foreach (var operation in Builtins.All.Values)
            {
                AddOperation(operation);
            }

            foreach (var word in Keywords.LanguageWords)
            {
                Add(word, KeywordKind, 4);
            }
        }

        private void AddVariables()
        {
            var extents = SemanticClassifier.FunctionExtents(_source);
            var enclosing = extents.FirstOrDefault(extent => extent.Contains(_cursor));
            if (enclosing is not null)
            {
                foreach (var parameter in enclosing.Signature.Parameters)
                {
                    Add(parameter.Name, VariableKind, 0, detail: $"{TreePrinter.Type(parameter.Type, _names)} {parameter.Name}");
                }
            }

            for (var index = 0; index < _tokens.Count; index++)
            {
                foreach (var (nameIndex, type) in NamesDeclaredAt(index))
                {
                    if (nameIndex == _at)
                    {
                        continue;
                    }

                    var name = _tokens[nameIndex];
                    var position = SemanticClassifier.At(name.Position);
                    var owner = extents.FirstOrDefault(extent => extent.Contains(position));
                    var isBeforeCursor = position.CompareTo(_cursor) < 0;
                    if (owner is null ? enclosing is not null || isBeforeCursor : owner == enclosing && isBeforeCursor)
                    {
                        Add(name.Text, VariableKind, 0, detail: $"{type} {name.Text}");
                    }
                }
            }
        }

        private IEnumerable<(int NameIndex, string Type)> NamesDeclaredAt(int index)
        {
            var token = _tokens[index];
            if (token.IsWord(Keywords.Def) || token.IsWord(Keywords.For) || token.IsWord(Keywords.IfSome))
            {
                var length = TypeLength(index + 1);
                var nameIndex = index + 1 + length;
                if (index + 1 < _tokens.Count && _tokens[index + 1].Kind == TokenKind.Word && nameIndex < _tokens.Count && IsPlainName(_tokens[nameIndex]))
                {
                    yield return (nameIndex, string.Join(" ", _tokens.Skip(index + 1).Take(length).Select(part => part.Text)));
                }
            }
            else if (token.IsWord(Keywords.When) && MatchedVariant(index + 1) is var (variant, first))
            {
                for (var offset = 0; offset < variant.Data.Count && first + offset < _tokens.Count; offset++)
                {
                    if (IsPlainName(_tokens[first + offset]))
                    {
                        yield return (first + offset, TreePrinter.Type(variant.Data[offset], _names));
                    }
                }
            }
        }

        private (ChoiceVariant Variant, int FirstName)? MatchedVariant(int index)
        {
            if (index >= _tokens.Count)
            {
                return null;
            }

            if (_tokens[index].IsWord(Keywords.From) && index + 2 < _tokens.Count
                && _names.Uses.TryGetValue(_tokens[index + 1].Text, out var use)
                && use.Target.Names.OwnVariant(_tokens[index + 2].Text) is { } qualified)
            {
                return (qualified, index + 3);
            }

            return _names.FindVariant(_tokens[index].Text) is { } variant ? (variant, index + 1) : null;
        }

        private static bool IsPlainName(Token token) => token.Kind == TokenKind.Word && !Keywords.IsReserved(token.Text);
    }
}
