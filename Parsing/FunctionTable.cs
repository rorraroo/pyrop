using Pyrop.Runtime;

namespace Pyrop.Parsing;

public sealed record Parameter(PyropType Type, string Name, SourcePosition Position);

public sealed record FunctionSignature(
    PyropType ReturnType, string Name, IReadOnlyList<Parameter> Parameters, SourcePosition Position, Module Module, bool IsExported)
{
    public int Arity => Parameters.Count;
}

public sealed class FunctionTable
{
    private readonly IReadOnlyDictionary<string, FunctionSignature> _functions;

    public static FunctionTable Empty { get; } = new(new Dictionary<string, FunctionSignature>());

    private FunctionTable(IReadOnlyDictionary<string, FunctionSignature> functions)
    {
        _functions = functions;
    }

    public FunctionSignature? Find(string name) => _functions.GetValueOrDefault(name);

    public bool Contains(string name) => _functions.ContainsKey(name);

    public IEnumerable<FunctionSignature> Signatures => _functions.Values;

    public FunctionTable With(IEnumerable<FunctionSignature> signatures)
    {
        var combined = new Dictionary<string, FunctionSignature>(_functions);
        foreach (var signature in signatures)
        {
            combined.Add(signature.Name, signature);
        }

        return new FunctionTable(combined);
    }
}
