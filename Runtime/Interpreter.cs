using Pyrop.Checking;
using Pyrop.Syntax;

namespace Pyrop.Runtime;

public sealed class Interpreter(TextReader input, IProgramOutput output)
{
    public const int MaxCallDepth = 10_000;

    private readonly TextReader _input = input;
    private readonly Scope _globals = new(null);
    private readonly Dictionary<string, FunctionStatement> _functions = new();
    private readonly Dictionary<BuiltinCallExpression, BuiltinOverload> _overloads = new(ReferenceEqualityComparer.Instance);
    private int _callDepth;

    public void Execute(SyntaxTree tree, CheckResult check)
    {
        foreach (var function in tree.Functions)
        {
            _functions[function.Signature.Name] = function;
        }

        foreach (var (call, overload) in check.Overloads)
        {
            _overloads[call] = overload;
        }

        _callDepth = 0;
        foreach (var statement in tree.Statements)
        {
            var result = ExecuteStatement(statement, _globals);
            if (result.Kind != CompletionKind.Normal)
            {
                throw Internal($"'{result.Kind}' escaped to the top level.", statement.Position);
            }
        }
    }

    private enum CompletionKind
    {
        Normal,
        Return,
        Break,
        Continue,
    }

    private readonly record struct Completion(CompletionKind Kind, Value? Value = null)
    {
        public static Completion Normal { get; } = new(CompletionKind.Normal);
    }

    private Completion ExecuteStatement(Statement statement, Scope scope)
    {
        switch (statement)
        {
            case DefStatement def:
                scope.Declare(def.Name, def.Type);
                return Completion.Normal;
            case SetStatement set:
                ExecuteSet(set, scope);
                return Completion.Normal;
            case IfStatement conditional:
                if (EvaluateCondition(conditional.Condition, scope))
                {
                    return ExecuteStatement(conditional.Then, scope);
                }

                return conditional.Else is null ? Completion.Normal : ExecuteStatement(conditional.Else, scope);
            case WhileStatement loop:
                return ExecuteWhile(loop, scope);
            case BreakStatement:
                return new Completion(CompletionKind.Break);
            case ContinueStatement:
                return new Completion(CompletionKind.Continue);
            case FunctionStatement:
                return Completion.Normal;
            case ReturnStatement returnStatement:
                return new Completion(
                    CompletionKind.Return,
                    returnStatement.Value is null ? null : Evaluate(returnStatement.Value, scope));
            case WriteStatement write:
                var text = Evaluate(write.Value, scope).ToText();
                output.Write(write.AppendNewLine ? text + "\n" : text);
                return Completion.Normal;
            case BlockStatement block:
                return ExecuteBlock(block, new Scope(scope));
            case CallStatement call:
                EvaluateCall(call.Call, scope);
                return Completion.Normal;
            default:
                throw Internal($"Unknown statement {statement.GetType().Name}.", statement.Position);
        }
    }

    private Completion ExecuteBlock(BlockStatement block, Scope scope)
    {
        foreach (var statement in block.Statements)
        {
            var result = ExecuteStatement(statement, scope);
            if (result.Kind != CompletionKind.Normal)
            {
                return result;
            }
        }

        return Completion.Normal;
    }

    private Completion ExecuteWhile(WhileStatement loop, Scope scope)
    {
        while (EvaluateCondition(loop.Condition, scope))
        {
            var result = ExecuteStatement(loop.Body, scope);
            switch (result.Kind)
            {
                case CompletionKind.Break:
                    return Completion.Normal;
                case CompletionKind.Return:
                    return result;
            }
        }

        return Completion.Normal;
    }

    private void ExecuteSet(SetStatement set, Scope scope)
    {
        var value = Evaluate(set.Value, scope);
        var variable = scope.Find(set.Name)
            ?? throw Runtime($"Variable '{set.Name}' does not exist yet; its def has not run.", set.Position);
        if (variable.Type != value.Type)
        {
            throw Internal($"Type mismatch assigning {value.Type.Name()} to {variable.Type.Name()} variable '{set.Name}'.", set.Position);
        }

        variable.Value = value;
    }

    private bool EvaluateCondition(Expression condition, Scope scope) =>
        Evaluate(condition, scope) is BoolValue boolean
            ? boolean.Value
            : throw Internal("A condition did not evaluate to bool.", condition.Position);

    private void EvaluateCall(Expression call, Scope scope)
    {
        if (call is FunctionCallExpression function)
        {
            CallFunction(function, scope);
        }
        else
        {
            Evaluate(call, scope);
        }
    }

    private Value Evaluate(Expression expression, Scope scope)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value;
            case VariableExpression variable:
                var slot = scope.Find(variable.Name)
                    ?? throw Runtime($"Variable '{variable.Name}' does not exist yet; its def has not run.", variable.Position);
                return slot.Value ?? throw Runtime($"Variable '{variable.Name}' is used before it is set.", variable.Position);
            case BuiltinCallExpression call:
                return CallBuiltin(call, scope);
            case FunctionCallExpression call:
                return CallFunction(call, scope)
                    ?? throw Internal($"Function '{call.Function.Name}' returned no value.", call.Position);
            case ConvertExpression convert:
                return Conversions.Convert(
                    Evaluate(convert.Operand, scope),
                    convert.Target,
                    message => Runtime(message, convert.Position));
            default:
                throw Internal($"Unknown expression {expression.GetType().Name}.", expression.Position);
        }
    }

    private Value CallBuiltin(BuiltinCallExpression call, Scope scope)
    {
        if (!_overloads.TryGetValue(call, out var overload))
        {
            throw Internal($"No overload was resolved for '{call.Operation.Name}'.", call.Position);
        }

        return overload.Implementation(new BuiltinInvocation(this, call, scope));
    }

    private Value? CallFunction(FunctionCallExpression call, Scope scope)
    {
        var signature = call.Function;
        var arguments = call.Arguments.Select(argument => Evaluate(argument, scope)).ToList();

        if (_callDepth >= MaxCallDepth)
        {
            throw Runtime($"Call depth limit of {MaxCallDepth} exceeded; check '{signature.Name}' for runaway recursion.", call.Position);
        }

        if (!_functions.TryGetValue(signature.Name, out var function))
        {
            throw Internal($"Function '{signature.Name}' is not registered.", call.Position);
        }

        var functionScope = new Scope(_globals);
        for (var index = 0; index < arguments.Count; index++)
        {
            var parameter = signature.Parameters[index];
            functionScope.Declare(parameter.Name, parameter.Type);
            functionScope.Find(parameter.Name)!.Value = arguments[index];
        }

        _callDepth++;
        try
        {
            var result = ExecuteStatement(function.Body, functionScope);
            if (result.Kind == CompletionKind.Return)
            {
                return result.Value;
            }

            if (signature.ReturnType != PyropType.Void)
            {
                throw Internal($"Function '{signature.Name}' finished without returning a value.", call.Position);
            }

            return null;
        }
        finally
        {
            _callDepth--;
        }
    }

    private static PyropException Runtime(string message, SourcePosition position) =>
        new(ErrorStage.Runtime, message, position);

    private static PyropException Internal(string message, SourcePosition position) =>
        new(ErrorStage.Internal, message, position);

    private sealed class BuiltinInvocation(Interpreter interpreter, BuiltinCallExpression call, Scope scope) : IBuiltinInvocation
    {
        private readonly Value?[] _arguments = new Value?[call.Arguments.Count];

        public Value Argument(int index) =>
            _arguments[index] ??= interpreter.Evaluate(call.Arguments[index], scope);

        public TextReader Input => interpreter._input;

        public Exception Fail(string message) => Runtime(message, call.Position);
    }
}
