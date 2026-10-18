using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public sealed record QualifiedName(Token From, Token Module, Token Name)
{
    public SourcePosition Position => From.Position;

    public override string ToString() => $"{Keywords.From} {Module.Text} {Name.Text}";
}

public static class QualifiedNames
{
    public static QualifiedName Read(TokenCursor cursor)
    {
        var from = cursor.Advance();
        var module = cursor.ExpectWord($"a module name after '{Keywords.From}'");
        var name = cursor.ExpectWord($"a name after '{Keywords.From} {module.Text}'");
        return new QualifiedName(from, module, name);
    }

    public static bool NamesType(ModuleNamespace names, TokenCursor cursor)
    {
        if (!cursor.Current.IsWord(Keywords.From) || cursor.Peek(1).Kind != TokenKind.Word || cursor.Peek(2).Kind != TokenKind.Word)
        {
            return false;
        }

        var name = cursor.Peek(2).Text;
        return names.Uses.TryGetValue(cursor.Peek(1).Text, out var use)
            ? use.Target.Names.Records.ContainsKey(name) || use.Target.Names.Choices.ContainsKey(name)
            : RecordDeclarations.IsTypeName(name) && names.Failures.OfName(cursor.Peek(1).Text) is not null;
    }

    public static object Resolve(ModuleNamespace names, QualifiedName qualified, TokenCursor cursor)
    {
        var use = FindModule(names, qualified, cursor);
        var module = qualified.Module.Text;
        var name = qualified.Name.Text;
        var target = use.Target.Names;
        if (target.Records.TryGetValue(name, out var record))
        {
            return Exported(record, record.IsExported, "Record", "declaration", use, qualified, cursor);
        }

        if (target.Choices.TryGetValue(name, out var choice))
        {
            return Exported(choice, choice.IsExported, "Choice", "declaration", use, qualified, cursor);
        }

        if (target.OwnVariant(name) is { } variant)
        {
            return ExportedVariant(variant, use, qualified, cursor);
        }

        if (target.Functions.Find(name) is { } function)
        {
            return Exported(function, function.IsExported, "Function", "definition", use, qualified, cursor);
        }

        throw cursor.Error(
            target.Imports.ContainsKey(name) || target.Variants.ContainsKey(name)
                ? $"Module '{module}' only imports '{name}' and cannot pass it on; use the module that exports it."
                : $"Module '{module}' has no function, record, choice or variant '{name}'.",
            qualified.Name);
    }

    public static DeclaredType ResolveType(ModuleNamespace names, QualifiedName qualified, TokenCursor cursor)
    {
        var use = FindModule(names, qualified, cursor);
        var module = qualified.Module.Text;
        var name = qualified.Name.Text;
        var target = use.Target.Names;
        if (target.Records.TryGetValue(name, out var record))
        {
            return Exported(record, record.IsExported, "Record", "declaration", use, qualified, cursor);
        }

        if (target.Choices.TryGetValue(name, out var choice))
        {
            return Exported(choice, choice.IsExported, "Choice", "declaration", use, qualified, cursor);
        }

        throw cursor.Error(
            target.Functions.Contains(name) ? $"'{name}' of module '{module}' is a function, not a type."
            : target.OwnVariant(name) is { } variant ? $"'{name}' of module '{module}' is a variant of the choice '{variant.Choice.Name}', not a type."
            : target.Imports.ContainsKey(name) || target.Variants.ContainsKey(name) ? $"Module '{module}' only imports '{name}' and cannot pass it on; use the module that exports it."
            : $"Module '{module}' has no record or choice '{name}'.",
            qualified.Name);
    }

    private static ModuleUse FindModule(ModuleNamespace names, QualifiedName qualified, TokenCursor cursor)
    {
        var module = qualified.Module.Text;
        if (!names.Uses.TryGetValue(module, out var use))
        {
            throw names.Failures.OfName(module) ?? cursor.Error(
                $"'{module}' is not a module of this file; load it with {Keywords.Use} {module} \"<path>\".", qualified.Module);
        }

        return use;
    }

    private static ChoiceVariant ExportedVariant(ChoiceVariant variant, ModuleUse use, QualifiedName qualified, TokenCursor cursor)
    {
        if (!variant.Choice.IsExported)
        {
            throw cursor.Error(
                $"Variant '{variant.Name}' of module '{qualified.Module.Text}' belongs to the choice '{variant.Choice.Name}', which is not exported; mark its declaration in {use.Target.Module.DisplayName} with {Keywords.Export}.",
                qualified.Name);
        }

        return variant;
    }

    private static T Exported<T>(
        T declaration, bool isExported, string kind, string place, ModuleUse use, QualifiedName qualified, TokenCursor cursor)
    {
        if (!isExported)
        {
            throw cursor.Error(
                $"{kind} '{qualified.Name.Text}' of module '{qualified.Module.Text}' is not exported; mark its {place} in {use.Target.Module.DisplayName} with {Keywords.Export}.",
                qualified.Name);
        }

        return declaration;
    }
}
