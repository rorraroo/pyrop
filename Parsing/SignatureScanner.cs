using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public static class SignatureScanner
{
    public static IReadOnlyList<FunctionSignature> Scan(IReadOnlyList<Token> tokens, FunctionTable existing, IncludeContext? includes = null)
    {
        var scan = new ScanState(existing, includes);
        scan.ScanFile(tokens, includes?.BaseDirectory, reportsIncompleteInput: true);
        return scan.Signatures;
    }

    private sealed class ScanState(FunctionTable existing, IncludeContext? includes)
    {
        private readonly HashSet<string> _names = new();
        private readonly HashSet<string> _visited = new(includes?.Included ?? [], SourceLoader.PathComparer);

        public List<FunctionSignature> Signatures { get; } = new();

        public void ScanFile(IReadOnlyList<Token> tokens, string? directory, bool reportsIncompleteInput)
        {
            var cursor = new TokenCursor(tokens, reportsIncompleteInput);
            while (!cursor.IsAtEnd)
            {
                var token = cursor.Advance();
                if (token.IsWord(Keywords.Function))
                {
                    AddSignature(ReadHeader(cursor, existing));
                }
                else if (includes is not null && directory is not null && token.IsWord(Keywords.Include) && cursor.Current.Kind == TokenKind.String)
                {
                    var path = cursor.Advance();
                    var file = includes.Loader.Load(path.Text, directory, path.Position);
                    if (_visited.Add(file.FullPath))
                    {
                        ScanFile(file.Tokens, file.Directory, reportsIncompleteInput: false);
                    }
                }
            }
        }

        private void AddSignature(FunctionSignature signature)
        {
            if (existing.Contains(signature.Name) || !_names.Add(signature.Name))
            {
                throw new PyropException(ErrorStage.Parse, $"Function '{signature.Name}' is already defined.", signature.Position);
            }

            Signatures.Add(signature);
        }
    }

    public static FunctionSignature ReadHeader(TokenCursor cursor, FunctionTable functions)
    {
        var returnType = TypeReader.ReadReturnType(cursor);

        var nameToken = cursor.Current;
        var name = cursor.ExpectIdentifier("function name", functions);

        var parameters = new List<Parameter>();
        while (true)
        {
            if (cursor.Current.IsWord(PyropType.Void.Name))
            {
                throw cursor.Error("A parameter cannot have type void.");
            }

            if (!TypeReader.StartsValueType(cursor.Current))
            {
                break;
            }

            var parameterType = TypeReader.ReadValueType(cursor, "parameter");
            var parameterToken = cursor.Current;
            var parameterName = cursor.ExpectIdentifier("parameter name", functions);
            if (parameterName == name)
            {
                throw cursor.Error($"Parameter '{parameterName}' has the same name as its function.", parameterToken);
            }

            if (parameters.Any(parameter => parameter.Name == parameterName))
            {
                throw cursor.Error($"Parameter '{parameterName}' is declared twice.", parameterToken);
            }

            parameters.Add(new Parameter(parameterType, parameterName, parameterToken.Position));
        }

        return new FunctionSignature(returnType, name, parameters, nameToken.Position);
    }
}
