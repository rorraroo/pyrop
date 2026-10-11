using Pyrop.Runtime;

namespace Pyrop.Checking;

public sealed class SymbolScope(SymbolScope? parent)
{
    private readonly Dictionary<string, PyropType> _symbols = new();

    public void Declare(string name, PyropType type) => _symbols[name] = type;

    public PyropType? Lookup(string name) =>
        _symbols.TryGetValue(name, out var type) ? type : parent?.Lookup(name);
}
