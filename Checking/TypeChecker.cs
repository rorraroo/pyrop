using Pyrop.Parsing;
using Pyrop.Runtime;
using Pyrop.Syntax;

namespace Pyrop.Checking;

public sealed record CheckResult(
    IReadOnlyDictionary<BuiltinCallExpression, BuiltinOverload> Overloads,
    IReadOnlyDictionary<AttemptExpression, PyropType> Attempts,
    IReadOnlyDictionary<string, PyropType> Globals);

public sealed record TypeFacts(
    IReadOnlyDictionary<Expression, PyropType> Types,
    IReadOnlyDictionary<BuiltinCallExpression, BuiltinOverload> Overloads)
{
    public static TypeFacts None { get; } = new(
        new Dictionary<Expression, PyropType>(ReferenceEqualityComparer.Instance),
        new Dictionary<BuiltinCallExpression, BuiltinOverload>(ReferenceEqualityComparer.Instance));
}

public sealed class TypeChecker
{
    private static readonly IReadOnlyDictionary<string, PyropType> NoGlobals = new Dictionary<string, PyropType>();

    private readonly List<PyropError> _errors = [];
    private readonly HashSet<string> _undeclared = [];
    private readonly Dictionary<BuiltinCallExpression, BuiltinOverload> _overloads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AttemptExpression, PyropType> _attempts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expression, PyropType> _types = new(ReferenceEqualityComparer.Instance);
    private Dictionary<string, PyropType> _globals = new();
    private FunctionSignature? _currentFunction;
    private ModuleNamespace? _names;

    private TypeChecker()
    {
    }

    public static CheckResult Check(ParsedProgram program, IReadOnlyDictionary<string, PyropType> sessionGlobals)
    {
        var checker = new TypeChecker();
        foreach (var module in program.Modules)
        {
            checker.CheckModule(module, NoGlobals);
        }

        var globals = checker.CheckModule(program.Main, sessionGlobals);
        if (checker._errors.Count > 0)
        {
            throw new PyropException(checker._errors);
        }

        return new CheckResult(checker._overloads, checker._attempts, globals);
    }

    public static TypeFacts Describe(ModuleTree module)
    {
        var checker = new TypeChecker();
        checker.CheckModule(module, NoGlobals);
        return new TypeFacts(checker._types, checker._overloads);
    }

    private Dictionary<string, PyropType> CheckModule(ModuleTree module, IReadOnlyDictionary<string, PyropType> sessionGlobals)
    {
        _names = module.Names;
        var tree = module.Tree;
        _globals = new Dictionary<string, PyropType>(sessionGlobals);
        foreach (var global in tree.Globals)
        {
            if (sessionGlobals.TryGetValue(global.Name, out var existing) && existing != global.Type)
            {
                var (earlierName, laterName) = Names(existing, global.Type);
                Report(
                    $"Global '{global.Name}' was declared as {earlierName} earlier in this session and cannot be redeclared as {laterName}.",
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

        return _globals;
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
            case IfSomeStatement unwrap:
                CheckIfSome(unwrap, scope);
                break;
            case MatchStatement match:
                CheckMatch(match, scope);
                break;
            case WhileStatement loop:
                CheckCondition(loop.Condition, scope, "while");
                CheckStatement(loop.Body, scope);
                break;
            case ForStatement loop:
                CheckFor(loop, scope);
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
            case ExitStatement exit:
                if (TypeOf(exit.Code, scope) is { } codeType && codeType != PyropType.Int)
                {
                    Report($"The exit code must be int, found {codeType.Name}.", exit.Code.Position);
                }

                break;
            case StyledStatement styled:
                CheckStyle(styled.Style, scope);
                CheckStatement(styled.Body, scope);
                break;
            case BlockStatement block:
                var blockScope = new SymbolScope(scope);
                foreach (var inner in block.Statements)
                {
                    CheckStatement(inner, blockScope);
                }

                break;
            case CallStatement call:
                var reported = _errors.Count;
                TypeOf(call.Call, scope);
                if (call.Call is BuiltinCallExpression { Operation: { MayBeDiscarded: false } operation } && _errors.Count == reported)
                {
                    Report(
                        $"'{operation.Name}' only gives a result, and here the result is thrown away, so this line does nothing. " +
                        $"Use the result, as in {Keywords.Set} <name> {operation.Name} …, or {Keywords.WriteLine} {operation.Name} … to show it. " +
                        "If this line was meant as a value of the call before it, that call was given too many values.",
                        call.Position);
                }

                break;
            case BreakStatement or ContinueStatement or UseStatement or ImportStatement or RecordStatement or ChoiceStatement:
                break;
            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}.");
        }
    }

    private void CheckDef(DefStatement def, SymbolScope scope)
    {
        if (TypeOf(def.Value, scope) is { } actual && actual != def.Type)
        {
            var (declared, found) = Names(def.Type, actual);
            Report($"Cannot initialize {declared} variable '{def.Name}' with a value of type {found}.", def.Value.Position);
        }

        scope.Declare(def.Name, def.Type);
    }

    private void CheckSet(SetStatement set, SymbolScope scope)
    {
        var valueType = TypeOf(set.Value, scope);
        if (scope.Lookup(set.Name) is not { } variableType)
        {
            if (_undeclared.Add(set.Name))
            {
                Report($"Undeclared variable '{set.Name}'. Declare it first with {Keywords.Define} <type> {set.Name} <value>.", set.Position);
            }

            return;
        }

        if (valueType is { } actual && actual != variableType)
        {
            var (declared, found) = Names(variableType, actual);
            Report($"Cannot set {declared} variable '{set.Name}' to a value of type {found}.", set.Value.Position);
        }
    }

    private void CheckCondition(Expression condition, SymbolScope scope, string owner)
    {
        if (TypeOf(condition, scope) is { } type && type != PyropType.Bool)
        {
            Report($"The condition of '{owner}' must be bool, found {type.Name}.", condition.Position);
        }
    }

    private void CheckFor(ForStatement loop, SymbolScope scope)
    {
        var listType = TypeOf(loop.List, scope);
        if (listType is not null && !PyropTypes.IsList(listType))
        {
            Report(
                PyropTypes.IsMap(listType)
                    ? $"'{Keywords.For}' walks a list, found {listType.Name}. Walk a map's keys with keys_of."
                    : $"'{Keywords.For}' walks a list, found {listType.Name}.",
                loop.List.Position);
        }
        else if (listType is ConstructedType { Arguments: [var elementType] } && elementType != loop.VariableType)
        {
            var (declared, found) = Names(loop.VariableType, elementType);
            Report(
                $"The loop variable '{loop.Variable}' is {declared}, but the list holds {found} items.",
                loop.List.Position);
        }

        var loopScope = new SymbolScope(scope);
        loopScope.Declare(loop.Variable, loop.VariableType);
        CheckStatement(loop.Body, loopScope);
    }

    private void CheckIfSome(IfSomeStatement unwrap, SymbolScope scope)
    {
        var optionType = TypeOf(unwrap.Option, scope);
        if (optionType is not null && !PyropTypes.IsOption(optionType))
        {
            Report($"'{Keywords.IfSome}' unwraps an option, found {optionType.Name}.", unwrap.Option.Position);
        }
        else if (optionType is ConstructedType { Arguments: [var contentType] } && contentType != unwrap.ValueType)
        {
            var (declared, found) = Names(unwrap.ValueType, contentType);
            Report(
                $"The {Keywords.IfSome} value '{unwrap.Name}' is {declared}, but the option holds {found}.",
                unwrap.Option.Position);
        }

        var valueScope = new SymbolScope(scope);
        valueScope.Declare(unwrap.Name, unwrap.ValueType);
        CheckStatement(unwrap.Then, valueScope);
        if (unwrap.Else is not null)
        {
            CheckStatement(unwrap.Else, scope);
        }
    }

    private void CheckMatch(MatchStatement match, SymbolScope scope)
    {
        var valueType = TypeOf(match.Value, scope);
        var choice = valueType as ChoiceType;
        if (valueType is not null && choice is null)
        {
            Report($"'{Keywords.Match}' takes a choice, found {valueType.Name}.", match.Value.Position);
        }

        var handled = new HashSet<ChoiceVariant>();
        foreach (var branch in match.Branches)
        {
            var variant = branch.Variant;
            if (choice is not null && variant.Choice != choice)
            {
                var (expected, found) = Names(choice, variant.Choice);
                Report(
                    $"'{variant.Name}' is a variant of {found}, but this {Keywords.Match} is over {expected}.",
                    branch.Position);
            }
            else if (!handled.Add(variant))
            {
                Report($"The variant '{variant.Name}' is handled twice in this {Keywords.Match}.", branch.Position);
            }

            var branchScope = new SymbolScope(scope);
            foreach (var (name, type) in branch.Names.Zip(variant.Data))
            {
                branchScope.Declare(name, type);
            }

            CheckStatement(branch.Body, branchScope);
        }

        if (choice is not null && choice.Variants.Where(variant => !handled.Contains(variant)).Select(variant => variant.Name).ToList() is
            { Count: > 0 } missing)
        {
            var listed = missing.Count == 1 ? missing[0] : $"{string.Join(", ", missing.SkipLast(1))} and {missing[^1]}";
            Report(
                $"This {Keywords.Match} does not handle the {(missing.Count == 1 ? "variant" : "variants")} {listed} of {choice.Name}; " +
                $"a {Keywords.Match} needs a {Keywords.When} for every variant.",
                match.Position);
        }
    }

    private void CheckVariantArguments(VariantExpression variant, SymbolScope scope)
    {
        var data = variant.Variant.Data;
        if (Views.IsInStyle(variant.Variant)
            && variant.Arguments[0] is LiteralExpression { Value: StringValue { Value: var style } } literal
            && OutputStyles.Find(style) is null)
        {
            Report(OutputStyles.UnknownMessage(style), literal.Position);
        }

        if (Views.IsInColor(variant.Variant))
        {
            CheckColorLiteral(variant.Arguments[0]);
        }

        if (Views.IsColoredGrid(variant.Variant) && variant.Arguments[1] is MapOfExpression colors)
        {
            foreach (var entry in colors.Entries)
            {
                if (entry.Key is LiteralExpression { Value: StringValue { Value: var key } } && !OutputStyles.IsColorKey(key))
                {
                    Report(OutputStyles.ColorKeyMessage(key), entry.Key.Position);
                }

                CheckColorLiteral(entry.Value);
            }
        }

        for (var index = 0; index < variant.Arguments.Count; index++)
        {
            if (TypeOf(variant.Arguments[index], scope) is { } type && type != data[index])
            {
                var (declared, found) = Names(data[index], type);
                Report(
                    data.Count == 1
                        ? $"The variant '{variant.Variant.Name}' carries {declared}, found {found}."
                        : $"Value {index + 1} of the variant '{variant.Variant.Name}' must be {declared}, found {found}.",
                    variant.Arguments[index].Position);
            }
        }
    }

    private void CheckColorLiteral(Expression color)
    {
        if (color is LiteralExpression { Value: StringValue { Value: var name } } && OutputStyles.FindColor(name) is null)
        {
            Report(OutputStyles.UnknownColorMessage(name), color.Position);
        }
    }

    private PyropType? TypeOfAttempt(AttemptExpression attempt, SymbolScope scope)
    {
        var result = TypeOf(attempt.Operation, scope);
        if (result is null)
        {
            return attempt.Operation is BuiltinCallExpression { Operation.ReturnsVoid: true } ? PyropType.Bool : null;
        }

        var type = result == PyropType.Void ? PyropType.Bool : PyropTypes.OptionOf(result);
        _attempts[attempt] = type;
        return type;
    }

    private void CheckStyle(Expression style, SymbolScope scope)
    {
        if (TypeOf(style, scope) is { } type && type != PyropType.String)
        {
            Report($"The style of 'styled' must be string, found {type.Name}.", style.Position);
        }
        else if (style is LiteralExpression { Value: StringValue { Value: var name } } && OutputStyles.Find(name) is null)
        {
            Report(OutputStyles.UnknownMessage(name), style.Position);
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
        var undeclaredOutside = _undeclared.ToList();
        _undeclared.Clear();
        CheckStatement(function.Body, scope);
        _undeclared.Clear();
        _undeclared.UnionWith(undeclaredOutside);
        _currentFunction = null;

        if (function.Signature.ReturnType != PyropType.Void && !ReturnPaths.AlwaysReturns(function.Body))
        {
            Report(
                $"Function '{function.Signature.Name}' must return a value of type {function.Signature.ReturnType.Name} on every path.",
                function.Signature.Position);
        }
    }

    private void CheckReturn(ReturnStatement returnStatement, SymbolScope scope)
    {
        if (_currentFunction is null || returnStatement.Value is null)
        {
            return;
        }

        if (TypeOf(returnStatement.Value, scope) is { } type && type != _currentFunction.ReturnType)
        {
            var (declared, found) = Names(_currentFunction.ReturnType, type);
            Report(
                $"Function '{_currentFunction.Name}' must return {declared}, found {found}.",
                returnStatement.Value.Position);
        }
    }

    private PyropType? TypeOf(Expression expression, SymbolScope scope)
    {
        var type = TypeOfUnrecorded(expression, scope);
        if (type is not null)
        {
            _types[expression] = type;
        }

        return type;
    }

    private PyropType? TypeOfUnrecorded(Expression expression, SymbolScope scope)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value.Type;
            case VariableExpression variable:
                var type = scope.Lookup(variable.Name);
                if (type is null && _undeclared.Add(variable.Name))
                {
                    Report(
                        _names?.Uses.ContainsKey(variable.Name) == true
                            ? $"'{variable.Name}' is a module; name one of its exported functions with {Keywords.From} {variable.Name} <name>."
                            : _names?.LibraryHint(variable.Name) is { } hint
                            ? hint
                            : Keywords.UnderscoredSpelling(variable.Name) is { } spelling
                            ? Keywords.UnderscoreHint(variable.Name, spelling)
                            : $"Undeclared variable '{variable.Name}'.",
                        variable.Position);
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
                    var (source, target) = Names(from, convert.Target);
                    Report($"Cannot convert {source} to {target}.", convert.Position);
                }

                return convert.Target;
            case MapOfExpression map:
                foreach (var entry in map.Entries)
                {
                    CheckMapOfPart(map, "key", entry.Key, map.KeyType, scope);
                    CheckMapOfPart(map, "value", entry.Value, map.ValueType, scope);
                }

                return PyropTypes.MapOf(map.KeyType, map.ValueType);
            case ListOfExpression list:
                foreach (var item in list.Items)
                {
                    if (TypeOf(item, scope) is { } itemType && itemType != list.ElementType)
                    {
                        var (element, found) = Names(list.ElementType, itemType);
                        Report(
                            $"Every item of 'list_of {element}' must be {element}, found {found}.",
                            item.Position);
                    }
                }

                return PyropTypes.ListOf(list.ElementType);
            case VariantsOfExpression variants:
                return TypeOfVariants(variants);
            case ConstructExpression construct:
                CheckConstructArguments(construct, scope);
                return construct.Record;
            case GetFieldExpression read:
                return FieldOf($"{Keywords.The} … {Keywords.Of}", read.Field, read.FieldPosition, read.Record, TypeOf(read.Record, scope))?.Type;
            case WithFieldExpression change:
                return TypeOfWithField(change, scope);
            case VariantExpression variant:
                CheckVariantArguments(variant, scope);
                return variant.Variant.Choice;
            case AttemptExpression attempt:
                return TypeOfAttempt(attempt, scope);
            case NoneExpression none:
                return PyropTypes.OptionOf(none.ContentType);
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
            return results is [{ IsConcrete: true } result] ? result : null;
        }

        var known = argumentTypes.Select(type => type!).ToList();
        if (operation.FindOverload(known) is not { } match)
        {
            Report(
                $"'{operation.Name}' cannot take {string.Join(" ", PyropTypes.NamesOf([.. known]))}. It accepts: {operation.DescribeAccepted()}.",
                call.Position);
            return null;
        }

        _overloads[call] = match.Overload;
        if (call.Arguments is [_, LiteralExpression { Value: StringValue { Value: var layout } } literal]
            && TimeOperations.LiteralLayoutProblem(operation, layout) is { } problem)
        {
            Report(problem, literal.Position);
        }

        return match.Result;
    }

    private void CheckFunctionArguments(FunctionCallExpression call, SymbolScope scope)
    {
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var parameter = call.Function.Parameters[index];
            if (TypeOf(call.Arguments[index], scope) is { } type && type != parameter.Type)
            {
                var (declared, found) = Names(parameter.Type, type);
                Report(
                    $"Argument '{parameter.Name}' of '{call.Function.Name}' must be {declared}, found {found}.",
                    call.Arguments[index].Position);
            }
        }
    }

    private PyropType? TypeOfVariants(VariantsOfExpression variants)
    {
        if (variants.Target is not ChoiceType choice)
        {
            Report($"'{Keywords.VariantsOf}' lists the variants of a choice, and '{variants.Target.Name}' is not one.", variants.Position);
            return null;
        }

        if (choice.HasData)
        {
            Report(
                $"'{Keywords.VariantsOf}' cannot list '{choice.Name}': a variant that carries data, such as '{choice.Variants.First(variant => variant.Arity > 0).Name}', has no value without its data.",
                variants.Position);
            return null;
        }

        return PyropTypes.ListOf(choice);
    }

    private void CheckConstructArguments(ConstructExpression construct, SymbolScope scope)
    {
        for (var index = 0; index < construct.Arguments.Count; index++)
        {
            var field = construct.Record.Fields[construct.Labels?[index].FieldIndex ?? index];
            if (TypeOf(construct.Arguments[index], scope) is { } type && type != field.Type)
            {
                var (declared, found) = Names(field.Type, type);
                Report(
                    $"Field '{field.Name}' of '{construct.Record.Name}' must be {declared}, found {found}.",
                    construct.Arguments[index].Position);
            }
        }
    }

    private RecordField? FieldOf(string form, string field, SourcePosition fieldPosition, Expression record, PyropType? recordType)
    {
        switch (recordType)
        {
            case null:
                return null;
            case RecordType known when known.FindField(field) is { } found:
                return found;
            case RecordType known:
                var fields = known.Fields.Select(each => each.Name).ToList();
                var listed = fields.Count == 1 ? fields[0] : $"{string.Join(", ", fields.SkipLast(1))} and {fields[^1]}";
                Report(
                    $"Record '{known.Name}' has no field '{field}'; its {(fields.Count == 1 ? "field is" : "fields are")} {listed}.",
                    fieldPosition);
                return null;
            case var other:
                Report($"'{form}' takes a record, found {other.Name}.", record.Position);
                return null;
        }
    }

    private PyropType? TypeOfWithField(WithFieldExpression change, SymbolScope scope)
    {
        var recordType = TypeOf(change.Record, scope);
        var field = FieldOf(Keywords.Copy, change.Field, change.FieldPosition, change.Record, recordType);
        var valueType = TypeOf(change.Value, scope);
        if (field is null)
        {
            return null;
        }

        if (valueType is { } actual && actual != field.Type)
        {
            var (declared, found) = Names(field.Type, actual);
            Report($"Cannot set {declared} field '{change.Field}' to a value of type {found}.", change.Value.Position);
        }

        return recordType;
    }

    private void CheckMapOfPart(MapOfExpression map, string part, Expression expression, PyropType expected, SymbolScope scope)
    {
        if (TypeOf(expression, scope) is { } actual && actual != expected)
        {
            var names = PyropTypes.NamesOf(map.KeyType, map.ValueType, expected, actual);
            Report($"Every {part} of '{Keywords.MapOf} {names[0]} {names[1]}' must be {names[2]}, found {names[3]}.", expression.Position);
        }
    }

    private static (string First, string Second) Names(PyropType first, PyropType second)
    {
        var names = PyropTypes.NamesOf(first, second);
        return (names[0], names[1]);
    }
}
