using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Pyrop.Checking;
using Pyrop.Parsing;
using Pyrop.Syntax;

namespace Pyrop.Runtime;

public sealed class Interpreter(LineInput input, IProgramOutput output, HostContext host)
{
    public const int MaxCallDepth = 10_000;
    public const int MaxExitCode = 255;

    private readonly LineInput _input = input;
    private readonly IProgramOutput _output = output;
    private readonly HostContext _host = host;
    private readonly RandomSource _random = new();
    private readonly ShownScreen _shownScreen = new();
    private OutputStyle _style = OutputStyle.None;
    private readonly Dictionary<Module, Scope> _moduleScopes = new();
    private readonly Dictionary<FunctionSignature, FunctionStatement> _functions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BuiltinCallExpression, BuiltinOverload> _overloads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AttemptExpression, PyropType> _attempts = new(ReferenceEqualityComparer.Instance);
    private int _callDepth;

    internal int CollectionLimit { get; set; } = CollectionOperations.MaxCount;

    internal int NestingLimit { get; set; } = ValueEquality.MaxNesting;

    public int? Execute(ParsedProgram program, CheckResult check)
    {
        _shownScreen.Panels = null;
        try
        {
            ExecuteProgram(program, check);
            return null;
        }
        catch (ExitSignal exit)
        {
            return exit.Code;
        }
    }

    private void ExecuteProgram(ParsedProgram program, CheckResult check)
    {
        foreach (var module in program.All)
        {
            if (!_moduleScopes.ContainsKey(module.Module))
            {
                _moduleScopes[module.Module] = new Scope(null);
            }

            foreach (var function in module.Tree.Functions)
            {
                _functions[function.Signature] = function;
            }
        }

        foreach (var (call, overload) in check.Overloads)
        {
            _overloads[call] = overload;
        }

        foreach (var (attempt, type) in check.Attempts)
        {
            _attempts[attempt] = type;
        }

        _callDepth = 0;
        foreach (var module in program.All)
        {
            var scope = _moduleScopes[module.Module];
            foreach (var statement in module.Tree.Statements)
            {
                var result = ExecuteStatement(statement, scope);
                if (result.Kind != CompletionKind.Normal)
                {
                    throw Internal($"'{result.Kind}' escaped to the top level.", statement.Position);
                }
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

    private void RequireStack(SourcePosition position)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw Runtime("The program ran out of stack space because its calls are nested too deeply; check for runaway recursion.", position);
        }
    }

    private Completion ExecuteStatement(Statement statement, Scope scope)
    {
        RequireStack(statement.Position);
        switch (statement)
        {
            case DefStatement def:
                ExecuteDef(def, scope);
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
            case IfSomeStatement unwrap:
                return ExecuteIfSome(unwrap, scope);
            case MatchStatement match:
                return ExecuteMatch(match, scope);
            case WhileStatement loop:
                return ExecuteWhile(loop, scope);
            case ForStatement loop:
                return ExecuteFor(loop, scope);
            case BreakStatement:
                return new Completion(CompletionKind.Break);
            case ContinueStatement:
                return new Completion(CompletionKind.Continue);
            case FunctionStatement or UseStatement or ImportStatement or RecordStatement or ChoiceStatement:
                return Completion.Normal;
            case ReturnStatement returnStatement:
                return new Completion(
                    CompletionKind.Return,
                    returnStatement.Value is null ? null : Evaluate(returnStatement.Value, scope));
            case WriteStatement write:
                var text = TextForm.Of(Evaluate(write.Value, scope), message => Runtime(message, write.Position), NestingLimit);
                var line = write.AppendNewLine ? text + "\n" : text;
                if (write.ToStandardError)
                {
                    _output.WriteError(line, _style);
                }
                else
                {
                    _output.Write(line, _style);
                }

                return Completion.Normal;
            case ExitStatement exit:
                throw new ExitSignal(EvaluateExitCode(exit, scope));
            case StyledStatement styled:
                return ExecuteWithStyle(styled, scope);
            case BlockStatement block:
                return ExecuteBlock(block, new Scope(scope));
            case CallStatement call:
                EvaluateCall(call.Call, scope);
                return Completion.Normal;
            default:
                throw Internal($"Unknown statement {statement.GetType().Name}.", statement.Position);
        }
    }

    private Completion ExecuteWithStyle(StyledStatement styled, Scope scope)
    {
        var name = Evaluate(styled.Style, scope).AsString();
        var outer = _style;
        _style = OutputStyles.Find(name) ?? throw Runtime(OutputStyles.UnknownMessage(name), styled.Style.Position);
        try
        {
            return ExecuteStatement(styled.Body, scope);
        }
        finally
        {
            _style = outer;
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

    private Completion ExecuteIfSome(IfSomeStatement unwrap, Scope scope)
    {
        if (Evaluate(unwrap.Option, scope) is not OptionValue option)
        {
            throw Internal("The expression of an if_some did not evaluate to an option.", unwrap.Option.Position);
        }

        if (option.Content is not { } value)
        {
            return unwrap.Else is null ? Completion.Normal : ExecuteStatement(unwrap.Else, scope);
        }

        if (value.Type != unwrap.ValueType)
        {
            throw Internal(
                $"Type mismatch unwrapping {value.Type.Name} into {unwrap.ValueType.Name} value '{unwrap.Name}'.", unwrap.Position);
        }

        var valueScope = new Scope(scope);
        valueScope.Declare(unwrap.Name, unwrap.ValueType, value);
        return ExecuteStatement(unwrap.Then, valueScope);
    }

    private Completion ExecuteMatch(MatchStatement match, Scope scope)
    {
        if (Evaluate(match.Value, scope) is not ChoiceValue value)
        {
            throw Internal("The value of a match did not evaluate to a choice.", match.Value.Position);
        }

        var branch = match.Branches.FirstOrDefault(candidate => candidate.Variant.Choice == value.Choice && candidate.Variant.Index == value.Index)
            ?? throw Internal($"The match has no branch for the variant '{value.Name}'.", match.Position);
        var branchScope = new Scope(scope);
        for (var index = 0; index < branch.Names.Count; index++)
        {
            branchScope.Declare(branch.Names[index], branch.Variant.Data[index], value.Data[index]);
        }

        return ExecuteStatement(branch.Body, branchScope);
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

    private Completion ExecuteFor(ForStatement loop, Scope scope)
    {
        if (Evaluate(loop.List, scope) is not ListValue list)
        {
            throw Internal("The list of a for loop did not evaluate to a list.", loop.List.Position);
        }

        foreach (var item in list.Items)
        {
            if (item.Type != loop.VariableType)
            {
                throw Internal(
                    $"Type mismatch walking {loop.VariableType.Name} loop variable '{loop.Variable}' over {item.Type.Name}.",
                    loop.Position);
            }

            var iteration = new Scope(scope);
            iteration.Declare(loop.Variable, loop.VariableType, item);
            var result = ExecuteStatement(loop.Body, iteration);
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

    private void ExecuteDef(DefStatement def, Scope scope)
    {
        var value = Evaluate(def.Value, scope);
        if (value.Type != def.Type)
        {
            throw Internal($"Type mismatch initializing {def.Type.Name} variable '{def.Name}' with {value.Type.Name}.", def.Position);
        }

        scope.Declare(def.Name, def.Type, value);
    }

    private void ExecuteSet(SetStatement set, Scope scope)
    {
        var value = Evaluate(set.Value, scope);
        var variable = scope.Find(set.Name)
            ?? throw Runtime($"Variable '{set.Name}' does not exist yet; its define has not run.", set.Position);
        if (variable.Type != value.Type)
        {
            throw Internal($"Type mismatch assigning {value.Type.Name} to {variable.Type.Name} variable '{set.Name}'.", set.Position);
        }

        variable.Value = value;
    }

    private int EvaluateExitCode(ExitStatement exit, Scope scope)
    {
        var code = Evaluate(exit.Code, scope).AsInt();
        if (code < 0 || code > MaxExitCode)
        {
            throw Runtime($"Exit code {code} is out of range; it must be between 0 and {MaxExitCode}.", exit.Code.Position);
        }

        return (int)code;
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
        else if (call is BuiltinCallExpression builtin)
        {
            CallBuiltin(builtin, scope);
        }
        else
        {
            Evaluate(call, scope);
        }
    }

    private Value Evaluate(Expression expression, Scope scope)
    {
        RequireStack(expression.Position);
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value;
            case VariableExpression variable:
                var slot = scope.Find(variable.Name)
                    ?? throw Runtime($"Variable '{variable.Name}' does not exist yet; its define has not run.", variable.Position);
                return slot.Value;
            case BuiltinCallExpression call:
                return CallBuiltin(call, scope)
                    ?? throw Internal($"Operation '{call.Operation.Name}' returned no value.", call.Position);
            case FunctionCallExpression call:
                return CallFunction(call, scope)
                    ?? throw Internal($"Function '{call.Function.Name}' returned no value.", call.Position);
            case ConvertExpression convert:
                return Conversions.Convert(
                    Evaluate(convert.Operand, scope),
                    convert.Target,
                    message => Runtime(message, convert.Position),
                    NestingLimit,
                    collectionLimit: CollectionLimit);
            case MapOfExpression map:
                return CollectionOperations.MapOf(
                    PyropTypes.MapOf(map.KeyType, map.ValueType),
                    map.Entries.Select(entry => (Evaluate(entry.Key, scope), Evaluate(entry.Value, scope))),
                    CollectionLimit,
                    message => Runtime(message, map.Position));
            case ListOfExpression list:
                return CollectionOperations.ListOf(
                    list.ElementType,
                    list.Items.Select(item => Evaluate(item, scope)).ToList(),
                    CollectionLimit,
                    message => Runtime(message, list.Position));
            case VariantsOfExpression variants:
                var listed = (ChoiceType)variants.Target;
                return new ListValue(PyropTypes.ListOf(listed), [.. listed.Variants.Select(variant => (Value)listed.ValueOf(variant.Index))]);
            case ConstructExpression construct:
                return Construct(construct, scope);
            case GetFieldExpression read:
                return ReadField(read, scope);
            case WithFieldExpression change:
                return ChangeField(change, scope);
            case VariantExpression variant:
                return MakeVariant(variant, scope);
            case AttemptExpression attempt:
                return Attempt(attempt, scope);
            case NoneExpression none:
                return OptionValue.None(PyropTypes.OptionOf(none.ContentType));
            default:
                throw Internal($"Unknown expression {expression.GetType().Name}.", expression.Position);
        }
    }

    private ChoiceValue MakeVariant(VariantExpression variant, Scope scope)
    {
        var declared = variant.Variant;
        if (declared.Arity == 0)
        {
            return declared.Choice.ValueOf(declared.Index);
        }

        var data = ImmutableArray.CreateBuilder<Value>(declared.Arity);
        for (var index = 0; index < declared.Arity; index++)
        {
            var value = Evaluate(variant.Arguments[index], scope);
            if (value.Type != declared.Data[index])
            {
                throw Internal(
                    $"Type mismatch making the variant '{declared.Name}': value {index + 1} is {declared.Data[index].Name}, found {value.Type.Name}.",
                    variant.Arguments[index].Position);
            }

            data.Add(value);
        }

        return new ChoiceValue(declared.Choice, declared.Index, data.MoveToImmutable());
    }

    private RecordValue Construct(ConstructExpression construct, Scope scope)
    {
        var fields = construct.Record.Fields;
        var values = new Value[fields.Count];
        for (var index = 0; index < fields.Count; index++)
        {
            var field = construct.Labels?[index].FieldIndex ?? index;
            var value = Evaluate(construct.Arguments[index], scope);
            if (value.Type != fields[field].Type)
            {
                throw Internal(
                    $"Type mismatch constructing {construct.Record.Name}: field '{fields[field].Name}' is {fields[field].Type.Name}, found {value.Type.Name}.",
                    construct.Arguments[index].Position);
            }

            values[field] = value;
        }

        return new RecordValue(construct.Record, [.. values]);
    }

    private Value ReadField(GetFieldExpression read, Scope scope)
    {
        var (record, index) = FieldOf(read.Record, read.Field, scope);
        return record.Fields[index];
    }

    private RecordValue ChangeField(WithFieldExpression change, Scope scope)
    {
        var (record, index) = FieldOf(change.Record, change.Field, scope);
        var value = Evaluate(change.Value, scope);
        var field = record.Record.Fields[index];
        if (value.Type != field.Type)
        {
            throw Internal($"Type mismatch setting {field.Type.Name} field '{field.Name}' to {value.Type.Name}.", change.Position);
        }

        return record.With(index, value);
    }

    private (RecordValue Record, int Index) FieldOf(Expression expression, string field, Scope scope)
    {
        if (Evaluate(expression, scope) is not RecordValue record || record.Record.IndexOf(field) is not (var index and >= 0))
        {
            throw Internal($"The field '{field}' was read from a value that has no such field.", expression.Position);
        }

        return (record, index);
    }

    private Value? CallBuiltin(BuiltinCallExpression call, Scope scope) =>
        Overload(call).Implementation(new BuiltinInvocation(this, call, scope, null));

    private BuiltinOverload Overload(BuiltinCallExpression call) =>
        _overloads.TryGetValue(call, out var overload)
            ? overload
            : throw Internal($"No overload was resolved for '{call.Operation.Name}'.", call.Position);

    private Value Attempt(AttemptExpression attempt, Scope scope)
    {
        if (!_attempts.TryGetValue(attempt, out var type))
        {
            throw Internal("No result type was resolved for an attempt.", attempt.Position);
        }

        var failure = new OperationFailure();
        var operand = attempt.Operation is ConvertExpression converted ? Evaluate(converted.Operand, scope) : null;
        Value? result;
        try
        {
            result = attempt.Operation switch
            {
                BuiltinCallExpression call => Overload(call).Implementation(new BuiltinInvocation(this, call, scope, failure)),
                ConvertExpression convert when operand is not null =>
                    Conversions.Convert(
                        operand,
                        convert.Target,
                        _ => new OperationFailed(failure),
                        NestingLimit,
                        message => Runtime(message, convert.Position),
                        CollectionLimit),
                var other => throw Internal($"'attempt' of the unexpected expression {other.GetType().Name}.", other.Position),
            };
        }
        catch (OperationFailed failed) when (failed.Failure == failure)
        {
            return type == PyropType.Bool ? BoolValue.False : OptionValue.None(type);
        }

        return result switch
        {
            null when type == PyropType.Bool => BoolValue.True,
            { } value when PyropTypes.OptionOf(value.Type) == type => OptionValue.Some(value),
            _ => throw Internal($"An attempt of type {type.Name} produced {result?.Type.Name ?? "no value"}.", attempt.Position),
        };
    }

    private Value? CallFunction(FunctionCallExpression call, Scope scope)
    {
        var signature = call.Function;
        var arguments = call.Arguments.Select(argument => Evaluate(argument, scope)).ToList();

        if (_callDepth >= MaxCallDepth)
        {
            throw Runtime($"Call depth limit of {MaxCallDepth} exceeded; check '{signature.Name}' for runaway recursion.", call.Position);
        }

        if (!_functions.TryGetValue(signature, out var function) || !_moduleScopes.TryGetValue(signature.Module, out var moduleScope))
        {
            throw Internal($"Function '{signature.Name}' is not registered.", call.Position);
        }

        var functionScope = new Scope(moduleScope);
        for (var index = 0; index < arguments.Count; index++)
        {
            var parameter = signature.Parameters[index];
            functionScope.Declare(parameter.Name, parameter.Type, arguments[index]);
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

    private sealed class BuiltinInvocation(Interpreter interpreter, BuiltinCallExpression call, Scope scope, OperationFailure? failure)
        : IBuiltinInvocation
    {
        private readonly Value?[] _arguments = new Value?[call.Arguments.Count];

        public Value Argument(int index) =>
            _arguments[index] ??= interpreter.Evaluate(call.Arguments[index], scope);

        public LineInput Input => interpreter._input;

        public HostContext Host => interpreter._host;

        public RandomSource Random => interpreter._random;

        public IProgramOutput Output => interpreter._output;

        public OutputStyle Style => interpreter._style;

        public ShownScreen ShownScreen => interpreter._shownScreen;

        public int CollectionLimit => interpreter.CollectionLimit;

        public int NestingLimit => interpreter.NestingLimit;

        public Exception Fail(string message) =>
            failure is null ? Runtime(message, call.Position) : new OperationFailed(failure);

        public Exception FailAtLimit(string message) => Runtime(message, call.Position);
    }

    private sealed class OperationFailure;

    private sealed class OperationFailed(OperationFailure failure) : Exception
    {
        public OperationFailure Failure { get; } = failure;
    }

    private sealed class ExitSignal(int code) : Exception
    {
        public int Code { get; } = code;
    }
}
