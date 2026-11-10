using Pyrop.Lexing;
using Pyrop.Runtime;

namespace Pyrop.Parsing;

public sealed record ScanResult(ModuleNamespace Names, IReadOnlyList<LoadedModule> Loaded);

public static class SignatureScanner
{
    public static ScanResult Scan(
        IReadOnlyList<Token> tokens,
        ModuleNamespace existing,
        ModuleLoading? loading = null,
        IReadOnlyCollection<string>? globals = null)
    {
        var scan = new ScanState(loading, existing.Libraries);
        var names = scan.ScanModule(tokens, existing, loading?.BaseDirectory, isUsedModule: false, globals ?? []);
        return new ScanResult(names, scan.Loaded);
    }

    private sealed class ScanState(ModuleLoading? loading, BuiltinLibraries libraries)
    {
        private readonly List<Module> _inProgress = [];
        private readonly Dictionary<string, LoadedModule> _scanned = new(SourceLoader.PathComparer);

        public List<LoadedModule> Loaded { get; } = [];

        public ModuleNamespace ScanModule(
            IReadOnlyList<Token> tokens,
            ModuleNamespace existing,
            string? directory,
            bool isUsedModule,
            IReadOnlyCollection<string> globals)
        {
            var reportsIncompleteInput = !isUsedModule;
            _inProgress.Add(existing.Module);
            try
            {
                var failures = new ModuleFailures();
                var uses = ScanUses(new TokenCursor(tokens, reportsIncompleteInput), existing, directory, failures);
                var imports = ScanImports(
                    new TokenCursor(tokens, reportsIncompleteInput), existing.With([], uses, []).WithFailures(failures), failures);
                var knownTypes = new KnownTypes(existing.With([], uses, imports).WithFailures(failures), KnownTypes.DeclaredIn(tokens));
                var (declarations, choiceDeclarations) = ScanTypeNames(new TokenCursor(tokens, reportsIncompleteInput), existing, knownTypes);
                var records = declarations.Where(declaration => declaration.IsNew).Select(declaration => declaration.Record).ToList();
                var choices = choiceDeclarations.Where(declaration => declaration.IsNew).Select(declaration => declaration.Choice).ToList();
                var typeNames = existing.With([], uses, imports, records, choices).WithFailures(failures);
                ScanRecordFields(new TokenCursor(tokens, reportsIncompleteInput), declarations, typeNames);
                ScanChoiceData(new TokenCursor(tokens, reportsIncompleteInput), choiceDeclarations, typeNames);
                RecordDeclarations.CheckSelfContainment(records, choices);
                var functions = ScanHeaders(new TokenCursor(tokens, reportsIncompleteInput), typeNames, failures);
                if (isUsedModule)
                {
                    RecordDeclarations.CheckExposure(existing.Module, records, choices, functions);
                }

                CheckClashes(existing, functions, records, choices, uses, imports, globals, failures);
                return existing.With(functions, uses, imports, records, choices).WithFailures(failures);
            }
            finally
            {
                _inProgress.RemoveAt(_inProgress.Count - 1);
            }
        }

        private List<ModuleUse> ScanUses(TokenCursor cursor, ModuleNamespace existing, string? directory, ModuleFailures failures)
        {
            var uses = new List<ModuleUse>();
            var seen = new HashSet<string>();
            while (!cursor.IsAtEnd)
            {
                var token = cursor.Advance();
                var isLibrary = cursor.Peek(1).IsWord(Keywords.Library);
                if (!token.IsWord(Keywords.Use) || cursor.Current.Kind != TokenKind.Word || (cursor.Peek(1).Kind != TokenKind.String && !isLibrary))
                {
                    continue;
                }

                if (!isLibrary && (loading is null || directory is null))
                {
                    throw cursor.Error($"'{Keywords.Use}' is not available here.", token);
                }

                var nameToken = cursor.Advance();
                var pathToken = cursor.Advance();
                try
                {
                    if (Keywords.IsReserved(nameToken.Text))
                    {
                        throw cursor.Error(Keywords.NotAName(nameToken.Text, "module name"), nameToken);
                    }

                    if (!seen.Add(nameToken.Text))
                    {
                        throw cursor.Error($"Module name '{nameToken.Text}' is already used in this file.", nameToken);
                    }

                    var target = isLibrary
                        ? libraries.Find(nameToken.Text)?.Loaded ?? throw cursor.Error(libraries.Unknown(nameToken.Text), nameToken)
                        : Load(cursor, pathToken, directory!);
                    if (existing.Uses.TryGetValue(nameToken.Text, out var earlier))
                    {
                        if (earlier.Target.Module == target.Module)
                        {
                            continue;
                        }

                        throw cursor.Error(
                            $"Module name '{nameToken.Text}' already names {earlier.Target.Module.DisplayName}.", nameToken);
                    }

                    uses.Add(new ModuleUse(nameToken.Text, target, nameToken.Position, token));
                }
                catch (PyropException error)
                {
                    failures.Add(token, nameToken.Text, error);
                }
            }

            return uses;
        }

        private LoadedModule Load(TokenCursor cursor, Token pathToken, string directory)
        {
            var fullPath = SourceLoader.Resolve(pathToken.Text, directory, pathToken.Position);
            if (loading!.Loaded.TryGetValue(fullPath, out var loaded) || _scanned.TryGetValue(fullPath, out loaded))
            {
                return loaded;
            }

            var cycleStart = _inProgress.FindIndex(module =>
                module.FullPath is { } path && SourceLoader.PathComparer.Equals(path, fullPath));
            if (cycleStart >= 0)
            {
                throw cursor.Error(CycleMessage(_inProgress[cycleStart..]), pathToken);
            }

            var file = loading.Loader.Load(pathToken.Text, directory, pathToken.Position);
            var module = new Module(file.FullPath, file.DisplayName);
            var names = ScanModule(file.Tokens, ModuleNamespace.For(module, libraries), file.Directory, isUsedModule: true, []);
            var result = new LoadedModule(module, names, file);
            _scanned[fullPath] = result;
            Loaded.Add(result);
            return result;
        }

        private static string CycleMessage(IReadOnlyList<Module> cycle)
        {
            var steps = cycle
                .Select((module, index) => $"{module.DisplayName} uses {cycle[(index + 1) % cycle.Count].DisplayName}")
                .ToList();
            var described = steps.Count == 1 ? steps[0] : $"{string.Join(", ", steps.SkipLast(1))}, and {steps[^1]}";
            return $"The modules use each other in a cycle: {described}. Move what they share into a module of its own.";
        }

        private sealed record RecordDeclaration(RecordType Record, Token Name, int FieldsStart, bool IsNew);

        private sealed record ChoiceDeclaration(ChoiceType Choice, Token Start, Token Name, int VariantsStart, bool IsNew);

        private static (List<RecordDeclaration> Records, List<ChoiceDeclaration> Choices) ScanTypeNames(
            TokenCursor cursor, ModuleNamespace existing, KnownTypes knownTypes)
        {
            var records = new List<RecordDeclaration>();
            var choices = new List<ChoiceDeclaration>();
            Token? previous = null;
            while (!cursor.IsAtEnd)
            {
                var token = cursor.Advance();
                var exported = previous is not null && previous.IsWord(Keywords.Export);
                var before = previous;
                previous = token;
                if (token.IsWord(Keywords.Record))
                {
                    RecordDeclarations.RejectKeywordAsName(token, cursor, RecordDeclarations.CanStartRecord(cursor, knownTypes), knownTypes);
                    var nameToken = RecordDeclarations.ReadName(cursor);
                    var name = nameToken.Text;
                    CheckNewTypeName(nameToken, Keywords.Record, records, choices, existing, cursor);
                    var earlier = existing.Records.GetValueOrDefault(name);
                    var record = earlier ?? new RecordType(name, existing.Module, nameToken.Position, exported);
                    records.Add(new RecordDeclaration(record, nameToken, cursor.Index, earlier is null));
                }
                else if (token.IsWord(Keywords.Choice))
                {
                    RecordDeclarations.RejectKeywordAsName(token, cursor, ChoiceDeclarations.CanStartChoice(cursor), knownTypes);
                    var nameToken = ChoiceDeclarations.ReadName(cursor);
                    CheckNewTypeName(nameToken, Keywords.Choice, records, choices, existing, cursor);
                    var variantsStart = cursor.Index;
                    var variants = ChoiceDeclarations.ReadVariants(cursor, token, nameToken.Text, knownTypes);
                    choices.Add(DeclareChoice(token, nameToken, variantsStart, variants, exported, choices, existing, cursor));
                    previous = null;
                }
            }

            return (records, choices);
        }

        private static void CheckNewTypeName(
            Token nameToken,
            string kind,
            List<RecordDeclaration> records,
            List<ChoiceDeclaration> choices,
            ModuleNamespace existing,
            TokenCursor cursor)
        {
            var name = nameToken.Text;
            var asRecord = records.Any(declaration => declaration.Record.Name == name);
            var asChoice = choices.Any(declaration => declaration.Choice.Name == name);
            if (kind == Keywords.Record ? asRecord : asChoice)
            {
                throw cursor.Error($"{(kind == Keywords.Record ? "Record" : "Choice")} '{name}' is already declared.", nameToken);
            }

            var other = kind == Keywords.Record
                ? (asChoice || existing.Choices.ContainsKey(name) ? Keywords.Choice : null)
                : (asRecord || existing.Records.ContainsKey(name) ? Keywords.Record : null);
            if (other is not null)
            {
                throw cursor.Error($"'{name}' is already declared as a {other}.", nameToken);
            }
        }

        private static ChoiceDeclaration DeclareChoice(
            Token start,
            Token nameToken,
            int variantsStart,
            IReadOnlyList<ChoiceDeclarations.ReadVariant> variants,
            bool exported,
            List<ChoiceDeclaration> choices,
            ModuleNamespace existing,
            TokenCursor cursor)
        {
            var name = nameToken.Text;
            if (existing.Choices.GetValueOrDefault(name) is { } earlier)
            {
                if (!earlier.HasSameVariants(variants.Select(variant => variant.Declaration).ToList()))
                {
                    throw cursor.Error(
                        $"Choice '{name}' was declared earlier in this session as {Keywords.Choice} {name} {earlier.DescribeVariants()} {Keywords.End}, and cannot be declared again with other variants.",
                        nameToken);
                }

                return new ChoiceDeclaration(earlier, start, nameToken, variantsStart, IsNew: false);
            }

            foreach (var variant in variants.Select(variant => variant.Name))
            {
                var owner = choices.Select(declaration => declaration.Choice.FindVariant(variant.Text)).FirstOrDefault(found => found is not null)
                    ?? existing.OwnVariant(variant.Text);
                if (owner is not null)
                {
                    throw cursor.Error($"Variant '{variant.Text}' already belongs to the choice '{owner.Choice.Name}'.", variant);
                }
            }

            var choice = new ChoiceType(
                name, existing.Module, nameToken.Position, exported, variants.Select(variant => variant.Declaration).ToList());
            return new ChoiceDeclaration(choice, start, nameToken, variantsStart, IsNew: true);
        }

        private static void ScanChoiceData(TokenCursor cursor, List<ChoiceDeclaration> declarations, ModuleNamespace names)
        {
            foreach (var declaration in declarations.Where(declaration => declaration.Choice.HasData))
            {
                cursor.MoveTo(declaration.VariantsStart);
                var choice = declaration.Choice;
                var variants = ChoiceDeclarations.ReadVariants(cursor, declaration.Start, choice.Name, names);
                if (!choice.HasSameVariants(variants.Select(variant => variant.Declaration).ToList()))
                {
                    throw new InvalidOperationException($"The variants of choice '{choice.Name}' were read differently in the two passes.");
                }

                var data = variants.Select(variant => variant.Data).ToList();
                if (declaration.IsNew)
                {
                    foreach (var (variant, types) in choice.Variants.Zip(data))
                    {
                        variant.Define(types);
                    }
                }
                else if (!choice.HasSameData(data))
                {
                    throw cursor.Error(
                        $"Choice '{choice.Name}' was declared earlier in this session as {Keywords.Choice} {choice.Name} {choice.DescribeVariants()} {Keywords.End}, and cannot be declared again with other variants.",
                        declaration.Name);
                }
            }
        }

        private static void ScanRecordFields(TokenCursor cursor, List<RecordDeclaration> declarations, ModuleNamespace names)
        {
            foreach (var declaration in declarations)
            {
                cursor.MoveTo(declaration.FieldsStart);
                var record = declaration.Record;
                var fields = RecordDeclarations.ReadFields(cursor, names, record.Name);
                if (declaration.IsNew)
                {
                    record.Define(fields);
                }
                else if (!record.HasSameFields(fields))
                {
                    throw cursor.Error(
                        $"Record '{record.Name}' was declared earlier in this session as {Keywords.Record} {record.Name} {record.DescribeFields()}, and cannot be declared again with other fields.",
                        declaration.Name);
                }
            }
        }

        private static bool HeaderReads(TokenCursor cursor, int start, ModuleNamespace names)
        {
            try
            {
                ReadHeader(cursor, names, isExported: false);
                return true;
            }
            catch (PyropException)
            {
                return false;
            }
            finally
            {
                cursor.MoveTo(start);
            }
        }

        private static string? HeaderName(TokenCursor cursor, int start, ModuleNamespace names)
        {
            cursor.MoveTo(start);
            try
            {
                TypeReader.ReadReturnType(cursor, names);
            }
            catch (PyropException)
            {
                return null;
            }

            return cursor.Current.Kind == TokenKind.Word && !Keywords.IsReserved(cursor.Current.Text) ? cursor.Current.Text : null;
        }

        private static List<FunctionSignature> ScanHeaders(TokenCursor cursor, ModuleNamespace names, ModuleFailures failures)
        {
            var functions = new List<FunctionSignature>();
            Token? previous = null;
            while (!cursor.IsAtEnd)
            {
                var token = cursor.Advance();
                var exported = previous is not null && previous.IsWord(Keywords.Export);
                var before = previous;
                previous = token;
                if (!token.IsWord(Keywords.Function))
                {
                    continue;
                }

                var start = cursor.Index;
                if (!HeaderReads(cursor, start, names))
                {
                    RecordDeclarations.RejectKeywordAsName(token, cursor, cursor.Current.Kind == TokenKind.Word || cursor.IsAtEnd, KnownTypes.Of(names));
                }

                try
                {
                    var signature = ReadHeader(cursor, names, exported);
                    if (names.Functions.Contains(signature.Name) || functions.Any(function => function.Name == signature.Name))
                    {
                        throw new PyropException(ErrorStage.Parse, $"Function '{signature.Name}' is already defined.", signature.Position);
                    }

                    functions.Add(signature);
                }
                catch (PyropException error)
                {
                    failures.Add(token, HeaderName(cursor, start, names), error);
                    cursor.MoveTo(start);
                }
            }

            return functions;
        }

        private static List<ModuleImport> ScanImports(TokenCursor cursor, ModuleNamespace names, ModuleFailures failures)
        {
            var imports = new List<ModuleImport>();
            var seen = new HashSet<string>();
            while (!cursor.IsAtEnd)
            {
                var token = cursor.Advance();
                if (!token.IsWord(Keywords.Import) || !cursor.Current.IsWord(Keywords.From) || cursor.Peek(1).Kind != TokenKind.Word)
                {
                    continue;
                }

                var from = cursor.Advance();
                var module = cursor.Advance();
                var inStatement = new HashSet<string>();
                var first = imports.Count;
                var named = new HashSet<ChoiceType>();
                var repeated = new List<(ModuleImport Earlier, QualifiedName Name)>();
                while (cursor.Current.Kind == TokenKind.Word
                    && !cursor.Current.IsWord(Keywords.End)
                    && !Keywords.IsStatement(cursor.Current.Text))
                {
                    var qualified = new QualifiedName(from, module, cursor.Advance());
                    if (!inStatement.Add(qualified.Name.Text))
                    {
                        failures.Add(token, qualified.Name.Text, cursor.Error(
                            $"'{qualified.Name.Text}' is named twice in the {Keywords.Import} started at {token.Position.Spoken}; if the names were meant to stop before it, close the {Keywords.Import} with '{Keywords.End}'.",
                            qualified.Name));
                        continue;
                    }

                    var (target, earlier) = ScanImportedName(qualified, token, cursor, names, seen, imports, failures);
                    if (target is ChoiceVariant variant)
                    {
                        named.Add(variant.Choice);
                    }

                    if (earlier is not null)
                    {
                        repeated.Add((earlier, qualified));
                    }
                }

                foreach (var (earlier, qualified) in repeated)
                {
                    if (earlier.Choice is { } partial && !earlier.AllVariants && !named.Contains(partial))
                    {
                        failures.Add(token, qualified.Name.Text, cursor.Error(
                            $"'{partial.Name}' was imported earlier with only some of its variants; import the others by name, as in {Keywords.Import} {Keywords.From} {qualified.Module.Text} {string.Join(" ", partial.Variants.Where(missing => names.FindVariant(missing.Name) != missing).Select(missing => missing.Name))} {Keywords.End}.",
                            qualified.Name));
                    }
                }

                for (var index = first; index < imports.Count; index++)
                {
                    if (imports[index].Choice is { } choice && named.Contains(choice))
                    {
                        imports[index] = imports[index] with { AllVariants = false };
                    }
                }
            }

            return imports;
        }

        private static (object? Target, ModuleImport? Earlier) ScanImportedName(
            QualifiedName qualified,
            Token token,
            TokenCursor cursor,
            ModuleNamespace names,
            HashSet<string> seen,
            List<ModuleImport> imports,
            ModuleFailures failures)
        {
            var name = qualified.Name.Text;
            try
            {
                var target = QualifiedNames.Resolve(names, qualified, cursor);
                if (!seen.Add(name))
                {
                    throw cursor.Error($"'{name}' is already imported in this file.", qualified.Name);
                }

                if (names.Imports.TryGetValue(name, out var earlier))
                {
                    if (ReferenceEquals(earlier.Target, target))
                    {
                        return (target, earlier);
                    }

                    throw cursor.Error($"'{name}' is already imported in this file.", qualified.Name);
                }

                imports.Add(new ModuleImport(qualified.Module.Text, name, target, qualified.Name.Position, token));
                return (target, null);
            }
            catch (PyropException error)
            {
                failures.Add(token, name, error);
                return (null, null);
            }
        }

        private static void CheckClashes(
            ModuleNamespace existing,
            List<FunctionSignature> functions,
            List<RecordType> records,
            List<ChoiceType> choices,
            List<ModuleUse> uses,
            List<ModuleImport> imports,
            IReadOnlyCollection<string> globals,
            ModuleFailures failures)
        {
            bool IsFunction(string name) => existing.Functions.Contains(name) || functions.Any(function => function.Name == name);
            bool IsRecord(string name) => existing.Records.ContainsKey(name) || records.Any(record => record.Name == name);
            bool IsChoice(string name) => existing.Choices.ContainsKey(name) || choices.Any(choice => choice.Name == name);
            bool IsOwnVariant(string name) =>
                existing.OwnVariant(name) is not null || choices.Any(choice => choice.FindVariant(name) is not null);
            bool IsImportedVariant(string name) => existing.FindVariant(name) is { } variant && variant.Choice.Module != existing.Module;
            foreach (var function in functions)
            {
                var message =
                    existing.Imports.ContainsKey(function.Name) || IsImportedVariant(function.Name) ? $"'{function.Name}' is imported in this file and cannot also be a function name."
                    : existing.Records.ContainsKey(function.Name) ? $"'{function.Name}' is a record in this file and cannot also be a function name."
                    : existing.Choices.ContainsKey(function.Name) ? $"'{function.Name}' is a choice in this file and cannot also be a function name."
                    : existing.OwnVariant(function.Name) is not null ? $"'{function.Name}' is a variant in this file and cannot also be a function name."
                    : globals.Contains(function.Name) ? $"'{function.Name}' is already a global variable and cannot be used as a function name."
                    : null;
                if (message is not null)
                {
                    throw Clash(message, function.Position);
                }
            }

            foreach (var declared in records.Cast<DeclaredType>().Concat(choices))
            {
                var name = declared.Name;
                var kind = declared is ChoiceType ? Keywords.Choice : Keywords.Record;
                var message =
                    IsFunction(name) ? $"'{name}' is a function of this file and cannot also name a {kind}."
                    : existing.Imports.ContainsKey(name) || IsImportedVariant(name) ? $"'{name}' is imported in this file and cannot also name a {kind}."
                    : existing.OwnVariant(name) is not null ? $"'{name}' is a variant in this file and cannot also name a {kind}."
                    : globals.Contains(name) ? $"'{name}' is already a global variable and cannot be used as a {kind} name."
                    : null;
                if (message is not null)
                {
                    throw Clash(message, declared.Position);
                }
            }

            foreach (var variant in choices.SelectMany(choice => choice.Variants))
            {
                var name = variant.Name;
                var message =
                    IsFunction(name) ? $"'{name}' is a function of this file and cannot also be a variant."
                    : IsRecord(name) ? $"'{name}' is a record of this file and cannot also be a variant."
                    : IsChoice(name) ? $"'{name}' is a choice of this file and cannot also be a variant."
                    : existing.Imports.ContainsKey(name) || IsImportedVariant(name) ? $"'{name}' is imported in this file and cannot also be a variant."
                    : globals.Contains(name) ? $"'{name}' is already a global variable and cannot be used as a variant."
                    : null;
                if (message is not null)
                {
                    throw Clash(message, variant.Position);
                }
            }

            var importedNames = new HashSet<string>();
            var importedVariants = new Dictionary<string, ChoiceVariant>();

            string? ImportedVariantOwner(string name) =>
                importedVariants.TryGetValue(name, out var variant) ? variant.Choice.Name
                : IsImportedVariant(name) ? existing.FindVariant(name)?.Choice.Name
                : null;

            string? VariantClash(string name) =>
                IsFunction(name) ? "a function of this file"
                : IsRecord(name) ? "a record of this file"
                : IsChoice(name) ? "a choice of this file"
                : IsOwnVariant(name) ? "a variant of this file"
                : existing.Imports.ContainsKey(name) || importedNames.Contains(name) ? "already imported in this file"
                : ImportedVariantOwner(name) is { } owner ? $"a variant of the imported choice '{owner}'"
                : globals.Contains(name) ? "a global variable"
                : null;

            foreach (var import in imports)
            {
                var name = import.Name;
                var message =
                    IsFunction(name) ? $"'{name}' is a function of this file and cannot also be imported."
                    : IsRecord(name) ? $"'{name}' is a record of this file and cannot also be imported."
                    : IsChoice(name) ? $"'{name}' is a choice of this file and cannot also be imported."
                    : IsOwnVariant(name) ? $"'{name}' is a variant of this file and cannot also be imported."
                    : ImportedVariantOwner(name) is { } owner ? $"'{name}' is a variant of the imported choice '{owner}' and cannot also be imported."
                    : globals.Contains(name) ? $"'{name}' is already a global variable and cannot be used as an imported name."
                    : null;
                var clashing = import.BroughtVariants
                    .Select(variant => (variant.Name, Reason: VariantClash(variant.Name)))
                    .FirstOrDefault(found => found.Reason is not null);
                if (message is null && clashing.Reason is not null)
                {
                    message = $"Importing '{name}' would also import its variant '{clashing.Name}', but '{clashing.Name}' is {clashing.Reason}.";
                    failures.Add(import.Statement, clashing.Name, Clash(message, import.Position));
                }

                if (message is not null)
                {
                    failures.Add(import.Statement, name, Clash(message, import.Position));
                    continue;
                }

                importedNames.Add(name);
                foreach (var variant in import.BroughtVariants)
                {
                    importedVariants[variant.Name] = variant;
                }
            }
        }

        private static PyropException Clash(string message, SourcePosition position) =>
            new(ErrorStage.Parse, message, position);
    }

    public static FunctionSignature ReadHeader(TokenCursor cursor, ModuleNamespace names, bool isExported)
    {
        var returnType = TypeReader.ReadReturnType(cursor, names);

        var nameToken = cursor.Current;
        var name = cursor.ExpectIdentifier("function name", names);

        var parameters = new List<Parameter>();
        while (true)
        {
            if (cursor.Current.IsWord(PyropType.Void.Name))
            {
                throw cursor.Error("A parameter cannot have type void.");
            }

            if (!TypeReader.StartsValueType(cursor, names))
            {
                break;
            }

            var parameterType = TypeReader.ReadValueType(cursor, names, "parameter");
            RecordDeclarations.ExpectNameAfterRecordType(cursor, parameterType, "parameter");
            var parameterToken = cursor.Current;
            var parameterName = cursor.ExpectIdentifier("parameter name", names);
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

        return new FunctionSignature(returnType, name, parameters, nameToken.Position, names.Module, isExported);
    }
}
