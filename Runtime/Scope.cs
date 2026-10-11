namespace Pyrop.Runtime;

public sealed class Scope(Scope? parent)
{
    private readonly Dictionary<string, Variable> _variables = new();

    public void Declare(string name, PyropType type, Value value) => _variables[name] = new Variable(type, value);

    public Variable? Find(string name) =>
        _variables.TryGetValue(name, out var variable) ? variable : parent?.Find(name);
}

public sealed class Variable(PyropType type, Value value)
{
    public PyropType Type { get; } = type;

    public Value Value { get; set; } = value;
}
