using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Pyrop.Runtime;

public sealed class TextFormReader
{
    private readonly string _text;
    private readonly DeclaredType _target;
    private readonly Func<string, Exception> _fail;
    private readonly Func<string, Exception> _tooDeep;
    private readonly int _nestingLimit;
    private readonly int _collectionLimit;
    private readonly int _end;
    private int _position;

    private TextFormReader(
        string text, DeclaredType target, Func<string, Exception> fail, Func<string, Exception> tooDeep, int nestingLimit, int collectionLimit)
    {
        _text = text;
        _target = target;
        _fail = fail;
        _tooDeep = tooDeep;
        _nestingLimit = nestingLimit;
        _collectionLimit = collectionLimit;
        _end = text.Length;
        while (_end > 0 && char.IsWhiteSpace(text[_end - 1]))
        {
            _end--;
        }

        while (_position < _end && char.IsWhiteSpace(text[_position]))
        {
            _position++;
        }
    }

    public static Value Read(
        string text,
        DeclaredType target,
        Func<string, Exception> fail,
        Func<string, Exception> tooDeep,
        int nestingLimit = ValueEquality.MaxNesting,
        int collectionLimit = int.MaxValue)
    {
        var reader = new TextFormReader(text, target, fail, tooDeep, nestingLimit, collectionLimit);
        var value = reader.ReadValue(target, 0, inner: false);
        if (reader._position < reader._end)
        {
            throw reader.Expected("the end of the text");
        }

        return value;
    }

    private Value ReadValue(PyropType type, int depth, bool inner)
    {
        var composite = type is RecordType or ChoiceType { HasData: true } || PyropTypes.IsList(type) || PyropTypes.IsMap(type) || PyropTypes.IsOption(type);
        if (composite && (depth >= _nestingLimit || !RuntimeHelpers.TryEnsureSufficientExecutionStack()))
        {
            throw _tooDeep("This text is nested too deeply to be read.");
        }

        if (type == PyropType.Int)
        {
            return ReadInt();
        }

        if (type == PyropType.Float)
        {
            return ReadFloat();
        }

        if (type == PyropType.Bool)
        {
            return ReadWord("true") ? BoolValue.True : ReadWord("false") ? BoolValue.False : throw Expected("true or false");
        }

        if (type == PyropType.String)
        {
            return new StringValue(ReadQuoted());
        }

        if (type is RecordType record)
        {
            return inner ? Bracketed(() => ReadRecord(record, depth)) : ReadRecord(record, depth);
        }

        if (type is ChoiceType choice)
        {
            return ReadChoice(choice, depth, inner);
        }

        if (type is ConstructedType constructed && constructed.Constructor == PyropTypes.List)
        {
            return ReadList(constructed, depth);
        }

        if (type is ConstructedType map && map.Constructor == PyropTypes.Map)
        {
            return ReadMap(map, depth);
        }

        if (type is ConstructedType option && option.Constructor == PyropTypes.Option)
        {
            return ReadOption(option, depth);
        }

        throw new InvalidOperationException($"No text form can be read as {type.Name}.");
    }

    private RecordValue ReadRecord(RecordType record, int depth)
    {
        Expect(record.Name, $"the record name '{record.Name}'");
        var fields = ImmutableArray.CreateBuilder<Value>(record.Fields.Count);
        foreach (var field in record.Fields)
        {
            Expect(" ", "a space");
            Expect(field.Name, $"the field '{field.Name}'");
            Expect(" ", "a space");
            fields.Add(ReadValue(field.Type, depth + 1, inner: true));
        }

        return new RecordValue(record, fields.MoveToImmutable());
    }

    private ChoiceValue ReadChoice(ChoiceType choice, int depth, bool inner)
    {
        if (inner && _position < _end && _text[_position] == '(')
        {
            return Bracketed(() => ReadChoiceWithData(choice, depth));
        }

        var start = _position;
        var variant = choice.FindVariant(ReadName());
        if (variant is null)
        {
            _position = start;
            throw Expected($"a variant of {choice.Name} ({choice.ListVariants()})");
        }

        if (variant.Arity == 0)
        {
            return choice.ValueOf(variant.Index);
        }

        if (inner)
        {
            _position = start;
            throw Expected($"'(' before the variant '{variant.Name}', which carries data");
        }

        return ReadData(choice, variant, depth);
    }

    private ChoiceValue ReadChoiceWithData(ChoiceType choice, int depth)
    {
        var start = _position;
        var variant = choice.FindVariant(ReadName());
        if (variant is not { Arity: > 0 })
        {
            _position = start;
            throw Expected($"a variant of {choice.Name} that carries data");
        }

        return ReadData(choice, variant, depth);
    }

    private ChoiceValue ReadData(ChoiceType choice, ChoiceVariant variant, int depth)
    {
        var data = ImmutableArray.CreateBuilder<Value>(variant.Arity);
        foreach (var type in variant.Data)
        {
            Expect(" ", "a space");
            data.Add(ReadValue(type, depth + 1, inner: true));
        }

        return new ChoiceValue(choice, variant.Index, data.MoveToImmutable());
    }

    private ListValue ReadList(ConstructedType type, int depth)
    {
        Expect("[", "'['");
        var items = ImmutableList.CreateBuilder<Value>();
        if (!TryRead("]"))
        {
            do
            {
                CollectionOperations.CheckRoom(items.Count + 1, _collectionLimit, "list", _fail);
                items.Add(ReadValue(type.Arguments[0], depth + 1, inner: false));
            }
            while (ReadSeparator("]"));
        }

        return new ListValue(type, items.ToImmutable());
    }

    private MapValue ReadMap(ConstructedType type, int depth)
    {
        Expect("{", "'{'");
        var map = MapValue.Empty(type);
        if (!TryRead("}"))
        {
            do
            {
                CollectionOperations.CheckRoom(map.Count + 1, _collectionLimit, "map", _fail);
                var keyStart = _position;
                var key = ReadValue(type.Arguments[0], depth + 1, inner: false);
                if (map.ContainsKey(key))
                {
                    _position = keyStart;
                    throw Expected("a key that is not already in the map");
                }

                Expect(": ", "': '");
                map = map.Put(key, ReadValue(type.Arguments[1], depth + 1, inner: false));
            }
            while (ReadSeparator("}"));
        }

        return map;
    }

    private OptionValue ReadOption(ConstructedType type, int depth)
    {
        if (ReadWord("none"))
        {
            return OptionValue.None(type);
        }

        if (!ReadWord("some"))
        {
            throw Expected("some or none");
        }

        Expect(" ", "a space");
        return OptionValue.Some(ReadValue(type.Arguments[0], depth + 1, inner: true));
    }

    private bool ReadSeparator(string closing)
    {
        if (TryRead(", "))
        {
            return true;
        }

        if (TryRead(closing))
        {
            return false;
        }

        throw Expected($"', ' or '{closing}'");
    }

    private T Bracketed<T>(Func<T> read)
    {
        Expect("(", "'('");
        var value = read();
        Expect(")", "')'");
        return value;
    }

    private IntValue ReadInt()
    {
        var start = _position;
        var length = NumberLength(allowFraction: false);
        var digits = _text.AsSpan(start, length).TrimStart('-');
        if (length == 0 || (digits.Length > 1 && digits[0] == '0') || _text.AsSpan(start, length) is "-0" || !long.TryParse(_text.AsSpan(start, length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            throw Expected("an int");
        }

        _position += length;
        return new IntValue(value);
    }

    private FloatValue ReadFloat()
    {
        var length = NumberLength(allowFraction: true);
        var span = _text.AsSpan(_position, length);
        if (length == 0
            || (span.IndexOfAny('.', 'E') < 0)
            || !double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !double.IsFinite(value))
        {
            throw Expected("a float, such as 1.0");
        }

        _position += length;
        return new FloatValue(value);
    }

    private int NumberLength(bool allowFraction)
    {
        var index = _position;
        if (index < _end && _text[index] == '-')
        {
            index++;
        }

        var digitsStart = index;
        index = Digits(index);
        if (index == digitsStart)
        {
            return 0;
        }

        if (allowFraction)
        {
            if (index < _end && _text[index] == '.')
            {
                var fractionStart = index + 1;
                index = Digits(fractionStart);
                if (index == fractionStart)
                {
                    return 0;
                }
            }

            if (index < _end && _text[index] == 'E')
            {
                var exponent = index + 1;
                if (exponent < _end && _text[exponent] is '+' or '-')
                {
                    exponent++;
                }

                var exponentEnd = Digits(exponent);
                if (exponentEnd == exponent)
                {
                    return 0;
                }

                index = exponentEnd;
            }
        }
        else if (index < _end && _text[index] is '.' or 'E')
        {
            return 0;
        }

        return index - _position;
    }

    private int Digits(int index)
    {
        while (index < _end && char.IsAsciiDigit(_text[index]))
        {
            index++;
        }

        return index;
    }

    private string ReadQuoted()
    {
        Expect("\"", "a string in quotes");
        var builder = new StringBuilder();
        while (true)
        {
            if (_position >= _end)
            {
                throw Expected("'\"' to close the string");
            }

            var character = _text[_position];
            if (character == '"')
            {
                _position++;
                return builder.ToString();
            }

            if (character == '\\')
            {
                var escaped = _position + 1 < _end ? _text[_position + 1] : '\0';
                builder.Append(escaped switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => throw Expected("one of the escapes \\\", \\\\, \\n, \\r or \\t"),
                });
                _position += 2;
                continue;
            }

            if (character is '\n' or '\r')
            {
                throw Expected("'\"' to close the string before the line ends");
            }

            builder.Append(character);
            _position++;
        }
    }

    private string ReadName()
    {
        var start = _position;
        while (_position < _end && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
        {
            _position++;
        }

        return _text[start.._position];
    }

    private bool ReadWord(string word)
    {
        if (string.CompareOrdinal(_text, _position, word, 0, word.Length) != 0 || _position + word.Length > _end)
        {
            return false;
        }

        var after = _position + word.Length;
        if (after < _end && (char.IsLetterOrDigit(_text[after]) || _text[after] == '_'))
        {
            return false;
        }

        _position = after;
        return true;
    }

    private bool TryRead(string expected)
    {
        if (_position + expected.Length > _end || string.CompareOrdinal(_text, _position, expected, 0, expected.Length) != 0)
        {
            return false;
        }

        _position += expected.Length;
        return true;
    }

    private void Expect(string expected, string description)
    {
        if (expected.Length > 0 && char.IsLetter(expected[0]) ? !ReadWord(expected) : !TryRead(expected))
        {
            throw Expected(description);
        }
    }

    private Exception Expected(string what)
    {
        var position = new StringInfo(_text[.._position]).LengthInTextElements;
        var article = "AEIOUaeiou".Contains(_target.Name[0]) ? "an" : "a";
        return _fail($"Cannot read {article} {_target.Name} at position {position}: expected {what}.");
    }
}
