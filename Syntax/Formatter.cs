using System.Text;
using Pyrop.Lexing;
using Pyrop.Parsing;

namespace Pyrop.Syntax;

public static class Formatter
{
    private const string Indent = "    ";

    public static string Format(string source, IReadOnlyList<Token> tokens, IReadOnlyList<Statement> statements)
    {
        var layout = new Layout([.. tokens.Where(token => token.Kind != TokenKind.EndOfInput)]);
        layout.Copies(statements.SelectMany(Expressions).OfType<WithFieldExpression>());
        layout.Place(statements, 0);
        return Render(source, layout);
    }

    private static IEnumerable<Expression> Expressions(Statement statement)
    {
        IEnumerable<Expression> own = statement switch
        {
            DefStatement def => [def.Value],
            SetStatement set => [set.Value],
            IfStatement conditional => [conditional.Condition],
            IfSomeStatement unwrap => [unwrap.Option],
            MatchStatement match => [match.Value],
            WhileStatement loop => [loop.Condition],
            ForStatement loop => [loop.List],
            ReturnStatement { Value: { } value } => [value],
            WriteStatement write => [write.Value],
            ExitStatement exit => [exit.Code],
            StyledStatement styled => [styled.Style],
            CallStatement call => [call.Call],
            _ => [],
        };
        IEnumerable<Statement> inner = statement switch
        {
            IfStatement conditional => conditional.Else is null ? [conditional.Then] : [conditional.Then, conditional.Else],
            IfSomeStatement unwrap => unwrap.Else is null ? [unwrap.Then] : [unwrap.Then, unwrap.Else],
            MatchStatement match => match.Branches.Select(branch => branch.Body),
            WhileStatement loop => [loop.Body],
            ForStatement loop => [loop.Body],
            StyledStatement styled => [styled.Body],
            FunctionStatement function => [function.Body],
            BlockStatement block => block.Statements,
            _ => [],
        };
        return own.SelectMany(Descendants).Concat(inner.SelectMany(Expressions));
    }

    private static IEnumerable<Expression> Descendants(Expression expression)
    {
        IEnumerable<Expression> children = expression switch
        {
            BuiltinCallExpression call => call.Arguments,
            FunctionCallExpression call => call.Arguments,
            ConstructExpression construct => construct.Arguments,
            VariantExpression variant => variant.Arguments,
            ConvertExpression convert => [convert.Operand],
            ListOfExpression list => list.Items,
            MapOfExpression map => map.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
            GetFieldExpression read => [read.Record],
            WithFieldExpression change => [change.Record, change.Value],
            AttemptExpression attempt => [attempt.Operation],
            _ => [],
        };
        return children.SelectMany(Descendants).Prepend(expression);
    }

    private static string Render(string source, Layout layout)
    {
        var lineEnd = source.IndexOf('\n') is var first and > 0 && source[first - 1] == '\r' ? "\r\n" : "\n";
        var lines = source.Split('\n').Select(line => line.Trim().Length == 0 ? "" : line.Trim()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var levels = new int[lines.Count];
        var following = 0;
        for (var index = lines.Count - 1; index >= 0; index--)
        {
            if (lines[index].Length == 0)
            {
                continue;
            }

            if (lines[index][0] == '#')
            {
                levels[index] = following;
                continue;
            }

            var number = index + 1;
            levels[index] = layout.LevelOf(number);
            following = layout.ClosesOrContinuesBody(number) ? levels[index] + 1 : levels[index];
        }

        var builder = new StringBuilder();
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Length > 0)
            {
                builder.Append(string.Concat(Enumerable.Repeat(Indent, levels[index]))).Append(lines[index]);
            }

            builder.Append(lineEnd);
        }

        return builder.ToString();
    }

    private sealed class Layout
    {
        private static readonly HashSet<string> Openers = [Keywords.Block, Keywords.ListOf, Keywords.MapOf, Keywords.Choice, Keywords.Import, Keywords.Match];
        private static readonly HashSet<string> Continued = [Keywords.ListOf, Keywords.MapOf, Keywords.Choice, Keywords.Import];

        private readonly IReadOnlyList<Token> _tokens;
        private readonly Dictionary<SourcePosition, int> _index = [];
        private readonly Dictionary<int, int> _firstOnLine = [];
        private readonly Dictionary<int, int> _opener = [];
        private readonly Dictionary<int, int> _openList = [];
        private readonly Dictionary<int, int> _levels = [];
        private readonly List<int> _statementStarts = [];
        private readonly Dictionary<int, int> _withOwner = [];

        public Layout(IReadOnlyList<Token> tokens)
        {
            _tokens = tokens;
            var open = new Stack<int>();
            for (var index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                _index[Key(token.Position)] = index;
                _firstOnLine.TryAdd(token.Position.Line, index);
                if (open.TryPeek(out var innermost) && Continued.Contains(tokens[innermost].Text))
                {
                    _openList[index] = innermost;
                }

                if (token.Kind != TokenKind.Word)
                {
                    continue;
                }

                if (token.Text == Keywords.End && open.TryPop(out var opener))
                {
                    _opener[index] = opener;
                }
                else if (Openers.Contains(token.Text))
                {
                    open.Push(index);
                }
            }
        }

        public void Copies(IEnumerable<WithFieldExpression> copies)
        {
            foreach (var copy in copies)
            {
                _withOwner[IndexOf(copy.FieldPosition) - 1] = IndexOf(copy.Position);
            }
        }

        public void Place(IEnumerable<Statement> statements, int level)
        {
            foreach (var statement in statements)
            {
                Place(statement, level);
            }
        }

        public int LevelOf(int line)
        {
            if (_levels.TryGetValue(line, out var known))
            {
                return known;
            }

            var first = _firstOnLine[line];
            var level = _opener.TryGetValue(first, out var opener) ? LevelOf(_tokens[opener].Position.Line)
                : _withOwner.TryGetValue(first, out var copy) && _tokens[copy].Position.Line < line ? LevelOf(_tokens[copy].Position.Line) + 1
                : _openList.TryGetValue(first, out var list) ? LevelOf(_tokens[list].Position.Line) + 1
                : StatementBefore(first) is { } start ? LevelOf(_tokens[start].Position.Line) + 1
                : 0;
            _levels[line] = level;
            return level;
        }

        public bool ClosesOrContinuesBody(int line)
        {
            var first = _tokens[_firstOnLine[line]];
            return first.IsWord(Keywords.End) || first.IsWord(Keywords.Else);
        }

        private void Place(Statement statement, int level)
        {
            var start = IndexOf(statement.Position);
            if (start > 0 && _tokens[start - 1].IsWord(Keywords.Export))
            {
                start--;
            }

            _statementStarts.Add(start);
            Start(start, level);
            var line = statement.Position.Line;
            switch (statement)
            {
                case IfStatement conditional:
                    Body(conditional.Then, line);
                    Else(conditional.Else, line);
                    break;
                case IfSomeStatement unwrap:
                    Body(unwrap.Then, line);
                    Else(unwrap.Else, line);
                    break;
                case WhileStatement loop:
                    Body(loop.Body, line);
                    break;
                case ForStatement loop:
                    Body(loop.Body, line);
                    break;
                case StyledStatement styled:
                    Body(styled.Body, line);
                    break;
                case FunctionStatement function:
                    Body(function.Body, line);
                    break;
                case BlockStatement block:
                    Place(block.Statements, LevelOf(line) + 1);
                    break;
                case MatchStatement match:
                    foreach (var branch in match.Branches)
                    {
                        var when = IndexOf(branch.Position);
                        _statementStarts.Add(when);
                        Start(when, LevelOf(line) + 1);
                        Body(branch.Body, branch.Position.Line);
                    }

                    break;
            }
        }

        private void Body(Statement body, int headerLine)
        {
            var level = LevelOf(headerLine);
            var ownLine = StartsLine(IndexOf(body.Position));
            Place(body, body is BlockStatement && ownLine ? level : level + 1);
        }

        private void Else(Statement? otherwise, int ifLine)
        {
            if (otherwise is null)
            {
                return;
            }

            var elseToken = IndexOf(otherwise.Position) - 1;
            Start(elseToken, LevelOf(ifLine));
            Body(otherwise, _tokens[elseToken].Position.Line);
        }

        private void Start(int index, int level)
        {
            if (StartsLine(index))
            {
                _levels.TryAdd(_tokens[index].Position.Line, level);
            }
        }

        private bool StartsLine(int index) => _firstOnLine[_tokens[index].Position.Line] == index;

        private int? StatementBefore(int index)
        {
            var found = _statementStarts.BinarySearch(index);
            var before = (found >= 0 ? found : ~found) - 1;
            return before >= 0 ? _statementStarts[before] : null;
        }

        private int IndexOf(SourcePosition position) => _index[Key(position)];

        private static SourcePosition Key(SourcePosition position) => position with { Source = null };
    }
}
