using System.Globalization;
using System.Text;

namespace Pyrop.Lexing;

public sealed class Lexer
{
    private readonly string _source;
    private int _index;
    private int _line = 1;
    private int _column = 1;

    private Lexer(string source)
    {
        _source = source;
    }

    public static IReadOnlyList<Token> Tokenize(string source) => new Lexer(source).TokenizeAll();

    private IReadOnlyList<Token> TokenizeAll()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespaceAndComments();
            if (IsAtEnd)
            {
                tokens.Add(new Token(TokenKind.EndOfInput, string.Empty, CurrentPosition));
                return tokens;
            }

            tokens.Add(ReadToken());
        }
    }

    private bool IsAtEnd => _index >= _source.Length;

    private char Current => _source[_index];

    private SourcePosition CurrentPosition => new(_line, _column);

    private char? PeekAhead(int offset) =>
        _index + offset < _source.Length ? _source[_index + offset] : null;

    private void Advance()
    {
        if (Current == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }

        _index++;
    }

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd)
        {
            if (char.IsWhiteSpace(Current))
            {
                Advance();
            }
            else if (Current == '#')
            {
                while (!IsAtEnd && Current != '\n')
                {
                    Advance();
                }
            }
            else
            {
                return;
            }
        }
    }

    private Token ReadToken()
    {
        var start = CurrentPosition;
        if (Current == '"')
        {
            return ReadString(start);
        }

        if (char.IsAsciiDigit(Current) || (Current == '-' && PeekAhead(1) is { } next && char.IsAsciiDigit(next)))
        {
            return ReadNumber(start);
        }

        if (IsWordStart(Current))
        {
            return ReadWord(start);
        }

        throw Error($"Unexpected character '{Current}'. Pyrop uses words instead of symbols.", start);
    }

    private static bool IsWordStart(char character) => char.IsLetter(character) || character == '_';

    private static bool IsWordPart(char character) => char.IsLetterOrDigit(character) || character == '_';

    private Token ReadWord(SourcePosition start)
    {
        var begin = _index;
        while (!IsAtEnd && IsWordPart(Current))
        {
            Advance();
        }

        return new Token(TokenKind.Word, _source[begin.._index], start);
    }

    private Token ReadNumber(SourcePosition start)
    {
        var begin = _index;
        if (Current == '-')
        {
            Advance();
        }

        SkipDigits();
        var isFloat = false;
        if (!IsAtEnd && Current == '.')
        {
            if (PeekAhead(1) is not { } afterDot || !char.IsAsciiDigit(afterDot))
            {
                throw Error("A float literal needs digits after the decimal point.", start);
            }

            isFloat = true;
            Advance();
            SkipDigits();
        }

        if (!IsAtEnd && (IsWordPart(Current) || Current == '.'))
        {
            throw Error($"Malformed number '{_source[begin.._index]}{Current}'.", start);
        }

        var text = _source[begin.._index];
        if (isFloat)
        {
            var value = double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            if (!double.IsFinite(value))
            {
                throw Error($"Float literal {text} is out of range.", start);
            }

            return new Token(TokenKind.Float, text, start);
        }

        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            throw Error($"Integer literal {text} does not fit in a 32-bit int.", start);
        }

        return new Token(TokenKind.Integer, text, start);
    }

    private void SkipDigits()
    {
        while (!IsAtEnd && char.IsAsciiDigit(Current))
        {
            Advance();
        }
    }

    private Token ReadString(SourcePosition start)
    {
        Advance();
        var builder = new StringBuilder();
        while (true)
        {
            if (IsAtEnd || Current == '\n' || Current == '\r')
            {
                throw Error("Unterminated string literal. A string must end on the line it starts on; use \\n for line breaks.", start);
            }

            if (Current == '"')
            {
                Advance();
                return new Token(TokenKind.String, builder.ToString(), start);
            }

            if (Current == '\\')
            {
                var escapePosition = CurrentPosition;
                Advance();
                if (IsAtEnd)
                {
                    throw Error("Unterminated string literal.", start);
                }

                builder.Append(Current switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    't' => '\t',
                    _ => throw Error($"Unknown escape sequence '\\{Current}'.", escapePosition),
                });
                Advance();
                continue;
            }

            builder.Append(Current);
            Advance();
        }
    }

    private static PyropException Error(string message, SourcePosition position) =>
        new(ErrorStage.Lex, message, position);
}
