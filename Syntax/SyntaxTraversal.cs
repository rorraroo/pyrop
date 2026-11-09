namespace Pyrop.Syntax;

public static class SyntaxTraversal
{
    public static IEnumerable<Expression> Children(Expression expression) => expression switch
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
}
