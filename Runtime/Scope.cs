namespace Pyrop.Runtime;

public sealed class Scope(Scope? parent)
{
    private readonly Dictionary<string, Variable> _variables = new();

    public void Declare(string name, PyropType type) => _variables[name] = new Variable(type);

    public Variable? Find(string name) =>
        _variables.TryGetValue(name, out var variable) ? variable : parent?.Find(name);
}

public sealed class Variable(PyropType type)
{
    public PyropType Type { get; } = type;

    public Value? Value { get; set; }
}
