namespace Pyrop.Runtime;

public sealed class Scope(Scope? parent)
{
    private Dictionary<string, Variable>? _variables;

    public void Declare(string name, PyropType type, Value value) =>
        (_variables ??= new Dictionary<string, Variable>())[name] = new Variable(type, value);

    public Variable? Find(string name)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope._variables is not null && scope._variables.TryGetValue(name, out var variable))
            {
                return variable;
            }
        }

        return null;
    }

    private Scope? Parent { get; } = parent;
}

public sealed class Variable(PyropType type, Value value)
{
    public PyropType Type { get; } = type;

    public Value Value { get; set; } = value;
}
