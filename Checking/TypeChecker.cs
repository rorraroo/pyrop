using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Checking;

public sealed record CheckResult(
    IReadOnlyDictionary<BuiltinCallExpression, BuiltinOverload> Overloads,
    IReadOnlyDictionary<string, PyropType> Globals);

public sealed class TypeChecker
{
    private readonly List<PyropError> _errors = [];
    private readonly Dictionary<BuiltinCallExpression, BuiltinOverload> _overloads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, PyropType> _globals;
    private FunctionSignature? _currentFunction;

    private TypeChecker(IReadOnlyDictionary<string, PyropType> sessionGlobals)
    {
        _globals = new Dictionary<string, PyropType>(sessionGlobals);
    }

    public static CheckResult Check(SyntaxTree tree, IReadOnlyDictionary<string, PyropType> sessionGlobals) =>
        new TypeChecker(sessionGlobals).CheckProgram(tree, sessionGlobals);

    private CheckResult CheckProgram(SyntaxTree tree, IReadOnlyDictionary<string, PyropType> sessionGlobals)
    {
        foreach (var global in tree.Globals)
        {
            if (sessionGlobals.TryGetValue(global.Name, out var existing) && existing != global.Type)
            {
                Report(
                    $"Global '{global.Name}' was declared as {existing.Name()} earlier in this session and cannot be redeclared as {global.Type.Name()}.",
                    global.Position);
            }

            _globals[global.Name] = global.Type;
        }

        var topLevel = new SymbolScope(null);
        foreach (var (name, type) in sessionGlobals)
        {
            topLevel.Declare(name, type);
        }

        foreach (var statement in tree.Statements)
        {
            CheckStatement(statement, topLevel);
        }

        if (_errors.Count > 0)
        {
            throw new PyropException(_errors);
        }

        return new CheckResult(_overloads, _globals);
    }

    private void Report(string message, SourcePosition position) =>
        _errors.Add(new PyropError(ErrorStage.Type, message, position));

    private void CheckStatement(Statement statement, SymbolScope scope)
    {
        switch (statement)
        {
            case DefStatement def:
                CheckDef(def, scope);
                break;
            case SetStatement set:
                CheckSet(set, scope);
                break;
            case IfStatement conditional:
                CheckCondition(conditional.Condition, scope, "if");
                CheckStatement(conditional.Then, scope);
                if (conditional.Else is not null)
                {
                    CheckStatement(conditional.Else, scope);
                }

                break;
            case WhileStatement loop:
                CheckCondition(loop.Condition, scope, "while");
                CheckStatement(loop.Body, scope);
                break;
            case FunctionStatement function:
                CheckFunction(function);
                break;
            case ReturnStatement returnStatement:
                CheckReturn(returnStatement, scope);
                break;
            case WriteStatement write:
                TypeOf(write.Value, scope);
                break;
            case BlockStatement block:
                var blockScope = new SymbolScope(scope);
                foreach (var inner in block.Statements)
                {
                    CheckStatement(inner, blockScope);
                }

                break;
            case CallStatement call:
                TypeOf(call.Call, scope);
                break;
            case IncludeStatement include:
                foreach (var inner in include.Included?.Statements ?? [])
                {
                    CheckStatement(inner, scope);
                }

                break;
            case BreakStatement or ContinueStatement:
                break;
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private void CheckDef(DefStatement def, SymbolScope scope)
    {
        if (TypeOf(def.Value, scope) is { } actual && actual != def.Type)
        {
            Report($"Cannot initialize {def.Type.Name()} variable '{def.Name}' with a {actual.Name()} value.", def.Value.Position);
        }

        scope.Declare(def.Name, def.Type);
    }

    private void CheckSet(SetStatement set, SymbolScope scope)
    {
        var valueType = TypeOf(set.Value, scope);
        if (scope.Lookup(set.Name) is not { } variableType)
        {
            Report($"Undeclared variable '{set.Name}'.", set.Position);
            return;
        }

        if (valueType is { } actual && actual != variableType)
        {
            Report($"Cannot set {variableType.Name()} variable '{set.Name}' to a {actual.Name()} value.", set.Value.Position);
        }
    }

    private void CheckCondition(Expression condition, SymbolScope scope, string owner)
    {
        if (TypeOf(condition, scope) is { } type && type != PyropType.Bool)
        {
            Report($"The condition of '{owner}' must be bool, found {type.Name()}.", condition.Position);
        }
    }

    private void CheckFunction(FunctionStatement function)
    {
        var globals = new SymbolScope(null);
        foreach (var (name, type) in _globals)
        {
            globals.Declare(name, type);
        }

        var scope = new SymbolScope(globals);
        foreach (var parameter in function.Signature.Parameters)
        {
            scope.Declare(parameter.Name, parameter.Type);
        }

        _currentFunction = function.Signature;
        CheckStatement(function.Body, scope);
        _currentFunction = null;

        if (function.Signature.ReturnType != PyropType.Void && !ReturnPaths.AlwaysReturns(function.Body))
        {
            Report(
                $"Function '{function.Signature.Name}' must return a value of type {function.Signature.ReturnType.Name()} on every path.",
                function.Signature.Position);
        }
    }

    private void CheckReturn(ReturnStatement returnStatement, SymbolScope scope)
    {
        if (_currentFunction is null)
        {
            return;
        }

        if (TypeOf(returnStatement.Value, scope) is { } type && type != _currentFunction.ReturnType)
        {
            Report(
                $"Function '{_currentFunction.Name}' must return {_currentFunction.ReturnType.Name()}, found {type.Name()}.",
                returnStatement.Value.Position);
        }
    }

    private PyropType? TypeOf(Expression expression, SymbolScope scope)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value.Type;
            case VariableExpression variable:
                var type = scope.Lookup(variable.Name);
                if (type is null)
                {
                    Report($"Undeclared variable '{variable.Name}'.", variable.Position);
                }

                return type;
            case BuiltinCallExpression call:
                return TypeOfBuiltinCall(call, scope);
            case FunctionCallExpression call:
                CheckFunctionArguments(call, scope);
                return call.Function.ReturnType;
            case ConvertExpression convert:
                if (TypeOf(convert.Operand, scope) is { } from && !Conversions.IsAllowed(from, convert.Target))
                {
                    Report($"Cannot convert {from.Name()} to {convert.Target.Name()}.", convert.Position);
                }

                return convert.Target;
            default:
                throw new InvalidOperationException($"Unknown expression {expression.GetType().Name}.");
        }
    }

    private PyropType? TypeOfBuiltinCall(BuiltinCallExpression call, SymbolScope scope)
    {
        var argumentTypes = call.Arguments.Select(argument => TypeOf(argument, scope)).ToList();
        var operation = call.Operation;
        if (argumentTypes.Any(type => type is null))
        {
            var results = operation.Overloads.Select(overload => overload.Result).Distinct().ToList();
            return results.Count == 1 ? results[0] : null;
        }

        var known = argumentTypes.Select(type => type!.Value).ToList();
        if (operation.FindOverload(known) is not { } overload)
        {
            var accepted = string.Join(", ", operation.Overloads.Select(candidate => candidate.DescribeParameters()));
            Report(
                $"'{operation.Name}' cannot take {string.Join(" ", known.Select(type => type.Name()))}. It accepts: {accepted}.",
                call.Position);
            return null;
        }

        _overloads[call] = overload;
        return overload.Result;
    }

    private void CheckFunctionArguments(FunctionCallExpression call, SymbolScope scope)
    {
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var parameter = call.Function.Parameters[index];
            if (TypeOf(call.Arguments[index], scope) is { } type && type != parameter.Type)
            {
                Report(
                    $"Argument '{parameter.Name}' of '{call.Function.Name}' must be {parameter.Type.Name()}, found {type.Name()}.",
                    call.Arguments[index].Position);
            }
        }
    }
}
