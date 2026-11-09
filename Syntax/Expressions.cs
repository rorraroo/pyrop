using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.Syntax;

public abstract record Expression(SourcePosition Position);

public sealed record LiteralExpression(Value Value, SourcePosition Position) : Expression(Position);

public sealed record VariableExpression(string Name, SourcePosition Position) : Expression(Position);

public sealed record BuiltinCallExpression(
    BuiltinOperation Operation, IReadOnlyList<Expression> Arguments, SourcePosition Position, string? Module = null)
    : Expression(Position);

public sealed record FunctionCallExpression(
    FunctionSignature Function, IReadOnlyList<Expression> Arguments, SourcePosition Position, string? Module = null)
    : Expression(Position);

public sealed record VariantsOfExpression(PyropType Target, SourcePosition Position) : Expression(Position);

public sealed record ConvertExpression(PyropType Target, Expression Operand, SourcePosition Position) : Expression(Position);

public sealed record ListOfExpression(PyropType ElementType, IReadOnlyList<Expression> Items, SourcePosition Position)
    : Expression(Position);

public sealed record MapEntry(Expression Key, Expression Value);

public sealed record MapOfExpression(PyropType KeyType, PyropType ValueType, IReadOnlyList<MapEntry> Entries, SourcePosition Position)
    : Expression(Position);

public sealed record FieldLabel(string Field, int FieldIndex, SourcePosition Position);

public sealed record ConstructExpression(
    RecordType Record, IReadOnlyList<Expression> Arguments, SourcePosition Position, string? Module = null, IReadOnlyList<FieldLabel>? Labels = null)
    : Expression(Position);

public sealed record GetFieldExpression(string Field, SourcePosition FieldPosition, Expression Record, SourcePosition Position)
    : Expression(Position);

public sealed record WithFieldExpression(
    string Field, SourcePosition FieldPosition, Expression Record, Expression Value, SourcePosition Position, bool Chained = false)
    : Expression(Position);

public sealed record VariantExpression(
    ChoiceVariant Variant, IReadOnlyList<Expression> Arguments, SourcePosition Position, string? Module = null) : Expression(Position);

public sealed record AttemptExpression(Expression Operation, SourcePosition Position) : Expression(Position);

public sealed record NoneExpression(PyropType ContentType, SourcePosition Position) : Expression(Position);
