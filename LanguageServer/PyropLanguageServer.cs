using System.Text.Json;
using Pyrop.Cli;
using Pyrop.Parsing;
using Pyrop.Runtime;

namespace Pyrop.LanguageServer;

public sealed class PyropLanguageServer
{
    public static readonly TimeSpan CheckDelay = TimeSpan.FromMilliseconds(250);

    private const int ParseError = -32700;
    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InternalError = -32603;
    private const int ServerNotInitialized = -32002;
    private const int FullTextSync = 1;
    private const int ErrorSeverity = 1;
    private const int TriggerCharacter = 2;

    private sealed record OpenDocument(string Uri, string Path, string Text);

    private readonly JsonRpcChannel _channel;
    private readonly TimeSpan _delay;
    private readonly object _gate = new();
    private readonly Dictionary<string, OpenDocument> _documents = new(SourceLoader.PathComparer);
    private readonly Dictionary<string, string> _uris = new(SourceLoader.PathComparer);
    private HashSet<string> _published = new(SourceLoader.PathComparer);
    private bool _initialized;
    private bool _shutDown;
    private bool _checkPending;
    private bool _stopping;
    private long _changedAt;

    private PyropLanguageServer(Stream input, Stream output, TimeSpan delay)
    {
        _channel = new JsonRpcChannel(input, output);
        _delay = delay;
    }

    public static int Run(Stream input, Stream output, TimeSpan delay) => new PyropLanguageServer(input, output, delay).Serve();

    private int Serve()
    {
        var worker = new Thread(CheckLoop) { IsBackground = true, Name = "Pyrop checks" };
        worker.Start();
        try
        {
            while (_channel.Read() is { } body)
            {
                if (Handle(body) is { } exitCode)
                {
                    return exitCode;
                }
            }

            return 1;
        }
        finally
        {
            lock (_gate)
            {
                _stopping = true;
                Monitor.PulseAll(_gate);
            }

            worker.Join();
        }
    }

    private int? Handle(byte[] body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            WriteError(null, ParseError, "The message is not valid JSON.");
            return null;
        }

        using (document)
        {
            var message = document.RootElement;
            if (message.ValueKind != JsonValueKind.Object)
            {
                WriteError(null, InvalidRequest, "A message must be a JSON object.");
                return null;
            }

            JsonElement? id = message.TryGetProperty("id", out var idElement) ? idElement : null;
            if (StringAt(message, "method") is not { } method)
            {
                if (id is not null && !message.TryGetProperty("result", out _) && !message.TryGetProperty("error", out _))
                {
                    WriteError(id, InvalidRequest, "A request must name its method.");
                }

                return null;
            }

            var parameters = message.TryGetProperty("params", out var parametersElement) ? parametersElement : default;
            try
            {
                if (id is { } requestId)
                {
                    HandleRequest(method, requestId, parameters);
                    return null;
                }

                return HandleNotification(method, parameters);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
            {
                Log($"Handling '{method}' failed: {exception.Message}");
                if (id is { } failedId)
                {
                    WriteError(failedId, InternalError, $"Handling '{method}' failed.");
                }

                return null;
            }
        }
    }

    private void HandleRequest(string method, JsonElement id, JsonElement parameters)
    {
        if (method == "initialize")
        {
            _initialized = true;
            WriteResult(id, writer =>
            {
                writer.WriteStartObject();
                writer.WriteStartObject("capabilities");
                writer.WriteStartObject("textDocumentSync");
                writer.WriteBoolean("openClose", true);
                writer.WriteNumber("change", FullTextSync);
                writer.WriteEndObject();
                writer.WriteStartObject("semanticTokensProvider");
                writer.WriteStartObject("legend");
                WriteStrings(writer, "tokenTypes", SemanticClassifier.KindNames);
                WriteStrings(writer, "tokenModifiers", SemanticClassifier.ModifierNames);
                writer.WriteEndObject();
                writer.WriteBoolean("full", true);
                writer.WriteEndObject();
                writer.WriteBoolean("hoverProvider", true);
                writer.WriteBoolean("definitionProvider", true);
                writer.WriteStartObject("completionProvider");
                WriteStrings(writer, "triggerCharacters", [" "]);
                writer.WriteEndObject();
                writer.WriteBoolean("documentFormattingProvider", true);
                writer.WriteEndObject();
                writer.WriteStartObject("serverInfo");
                writer.WriteString("name", "pyrop");
                writer.WriteString("version", Repl.Version);
                writer.WriteEndObject();
                writer.WriteEndObject();
            });
        }
        else if (!_initialized)
        {
            WriteError(id, ServerNotInitialized, "The server has not been initialized yet.");
        }
        else if (_shutDown)
        {
            WriteError(id, InvalidRequest, "The server has been shut down.");
        }
        else if (method == "textDocument/semanticTokens/full")
        {
            var tokens = SemanticTokensOf(StringAt(parameters, "textDocument", "uri"));
            WriteResult(id, writer => WriteSemanticTokens(writer, tokens));
        }
        else if (method == "textDocument/hover")
        {
            var hover = HoverOf(StringAt(parameters, "textDocument", "uri"), NumberAt(parameters, "position", "line"), NumberAt(parameters, "position", "character"));
            WriteResult(id, writer => WriteHover(writer, hover));
        }
        else if (method == "textDocument/definition")
        {
            var definition = DefinitionOf(StringAt(parameters, "textDocument", "uri"), NumberAt(parameters, "position", "line"), NumberAt(parameters, "position", "character"));
            WriteResult(id, writer => WriteDefinition(writer, definition));
        }
        else if (method == "textDocument/completion")
        {
            var entries = CompletionOf(
                StringAt(parameters, "textDocument", "uri"),
                NumberAt(parameters, "position", "line"),
                NumberAt(parameters, "position", "character"),
                NumberAt(parameters, "context", "triggerKind") == TriggerCharacter);
            WriteResult(id, writer => WriteCompletion(writer, entries));
        }
        else if (method == "textDocument/formatting")
        {
            var edit = FormattingOf(StringAt(parameters, "textDocument", "uri"));
            WriteResult(id, writer => WriteEdits(writer, edit));
        }
        else if (method == "shutdown")
        {
            _shutDown = true;
            WriteResult(id, writer => writer.WriteNullValue());
        }
        else
        {
            WriteError(id, MethodNotFound, $"Unknown method '{method}'.");
        }
    }

    private int? HandleNotification(string method, JsonElement parameters)
    {
        if (method == "exit")
        {
            return _shutDown ? 0 : 1;
        }

        if (!_initialized || _shutDown)
        {
            return null;
        }

        switch (method)
        {
            case "textDocument/didOpen":
                Open(StringAt(parameters, "textDocument", "uri"), StringAt(parameters, "textDocument", "text"));
                break;
            case "textDocument/didChange":
                Change(StringAt(parameters, "textDocument", "uri"), LastFullText(parameters));
                break;
            case "textDocument/didClose":
                Close(StringAt(parameters, "textDocument", "uri"));
                break;
            case "workspace/didChangeWatchedFiles":
                lock (_gate)
                {
                    ScheduleCheck();
                }

                break;
        }

        return null;
    }

    private void Open(string? uri, string? text)
    {
        if (text is null || PathOf(uri) is not { } path)
        {
            return;
        }

        lock (_gate)
        {
            _documents[path] = new OpenDocument(uri!, path, text);
            ScheduleCheck();
        }
    }

    private void Change(string? uri, string? text)
    {
        if (text is null || PathOf(uri) is not { } path)
        {
            return;
        }

        lock (_gate)
        {
            if (_documents.TryGetValue(path, out var document))
            {
                _documents[path] = document with { Text = text };
                ScheduleCheck();
            }
        }
    }

    private void Close(string? uri)
    {
        if (PathOf(uri) is not { } path)
        {
            return;
        }

        lock (_gate)
        {
            if (_documents.Remove(path))
            {
                ScheduleCheck();
            }
        }
    }

    private (OpenDocument Document, PyropSession Session)? SessionFor(string? uri)
    {
        OpenDocument? document;
        Dictionary<string, string> texts;
        lock (_gate)
        {
            if (PathOf(uri) is not { } path || !_documents.TryGetValue(path, out document))
            {
                return null;
            }

            texts = _documents.Values.ToDictionary(open => open.Path, open => open.Text, SourceLoader.PathComparer);
        }

        return (document, new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true), store: new DocumentSourceStore(texts)));
    }

    private IReadOnlyList<SemanticToken>? SemanticTokensOf(string? uri)
    {
        if (SessionFor(uri) is not var (document, session))
        {
            return null;
        }

        try
        {
            return session.Analyze(document.Text, document.Path, document.Path) is { } analyzed ? SemanticClassifier.Classify(analyzed) : null;
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            Log($"Highlighting {document.Path} failed: {exception.Message}");
            return null;
        }
    }

    private HoverResult? HoverOf(string? uri, int? line, int? character)
    {
        if (line is not { } zeroLine || character is not { } zeroCharacter || SessionFor(uri) is not var (document, session))
        {
            return null;
        }

        try
        {
            return session.Analyze(document.Text, document.Path, document.Path) is { } analyzed
                ? LargeStack.Run(() => HoverProvider.Describe(analyzed, zeroLine + 1, zeroCharacter + 1))
                : null;
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            Log($"Hover in {document.Path} failed: {exception.Message}");
            return null;
        }
    }

    private (string Uri, DefinitionTarget Target)? DefinitionOf(string? uri, int? line, int? character)
    {
        if (line is not { } zeroLine || character is not { } zeroCharacter || SessionFor(uri) is not var (document, session))
        {
            return null;
        }

        try
        {
            if (session.Analyze(document.Text, document.Path, document.Path) is not { } analyzed
                || LargeStack.Run(() => DefinitionProvider.Find(analyzed, zeroLine + 1, zeroCharacter + 1)) is not { } target)
            {
                return null;
            }

            return (UriOf(target.Path ?? document.Path), target);
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            Log($"Go to definition in {document.Path} failed: {exception.Message}");
            return null;
        }
    }

    private string UriOf(string path)
    {
        lock (_gate)
        {
            return _documents.TryGetValue(path, out var open) ? open.Uri : new Uri(path).AbsoluteUri;
        }
    }

    private IReadOnlyList<CompletionEntry> CompletionOf(string? uri, int? line, int? character, bool typedSpace)
    {
        if (line is not { } zeroLine || character is not { } zeroCharacter || SessionFor(uri) is not var (document, session))
        {
            return [];
        }

        try
        {
            return session.Analyze(document.Text, document.Path, document.Path) is { } analyzed
                ? LargeStack.Run(() => CompletionProvider.Complete(analyzed, zeroLine + 1, zeroCharacter + 1, typedSpace))
                : [];
        }
        catch (Exception exception) when (exception is not PyropException)
        {
            Log($"Completion in {document.Path} failed: {exception.Message}");
            return [];
        }
    }

    private (int Line, int Character, string Text)? FormattingOf(string? uri)
    {
        if (SessionFor(uri) is not var (document, session))
        {
            return null;
        }

        string formatted;
        try
        {
            formatted = session.Format(document.Text, document.Path, document.Path);
        }
        catch (PyropException)
        {
            return null;
        }
        catch (Exception exception)
        {
            Log($"Formatting {document.Path} failed: {exception.Message}");
            return null;
        }

        if (formatted == document.Text)
        {
            return null;
        }

        var (line, character) = EndOf(document.Text);
        return (line, character, formatted);
    }

    private static (int Line, int Character) EndOf(string text)
    {
        var line = 0;
        var lineStart = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n' || (text[index] == '\r' && (index + 1 == text.Length || text[index + 1] != '\n')))
            {
                line++;
                lineStart = index + 1;
            }
        }

        return (line, text.Length - lineStart);
    }

    private static void WriteHover(Utf8JsonWriter writer, HoverResult? hover)
    {
        if (hover is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteStartObject("contents");
        writer.WriteString("kind", "markdown");
        writer.WriteString("value", hover.Markdown);
        writer.WriteEndObject();
        writer.WriteStartObject("range");
        WritePosition(writer, "start", hover.Line, hover.Start);
        WritePosition(writer, "end", hover.Line, hover.Start + hover.Length);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteDefinition(Utf8JsonWriter writer, (string Uri, DefinitionTarget Target)? definition)
    {
        if (definition is not var (uri, target))
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("uri", uri);
        writer.WriteStartObject("range");
        WritePosition(writer, "start", target.Line, target.Start);
        WritePosition(writer, "end", target.Line, target.Start + target.Length);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteCompletion(Utf8JsonWriter writer, IReadOnlyList<CompletionEntry> entries)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("isIncomplete", false);
        writer.WriteStartArray("items");
        foreach (var entry in entries)
        {
            writer.WriteStartObject();
            writer.WriteString("label", entry.Label);
            writer.WriteNumber("kind", entry.Kind);
            writer.WriteString("sortText", $"{entry.Rank}{entry.Label}");
            if (entry.Takes is not null)
            {
                writer.WriteStartObject("labelDetails");
                writer.WriteString("description", entry.Takes);
                writer.WriteEndObject();
            }

            if (entry.Detail is not null)
            {
                writer.WriteString("detail", entry.Detail);
            }

            if (entry.Documentation is not null)
            {
                writer.WriteStartObject("documentation");
                writer.WriteString("kind", "markdown");
                writer.WriteString("value", entry.Documentation);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteEdits(Utf8JsonWriter writer, (int Line, int Character, string Text)? edit)
    {
        writer.WriteStartArray();
        if (edit is var (line, character, text))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("range");
            WritePosition(writer, "start", 0, 0);
            WritePosition(writer, "end", line, character);
            writer.WriteEndObject();
            writer.WriteString("newText", text);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteSemanticTokens(Utf8JsonWriter writer, IReadOnlyList<SemanticToken>? tokens)
    {
        if (tokens is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteStartArray("data");
        var line = 0;
        var start = 0;
        foreach (var token in tokens.OrderBy(token => token.Line).ThenBy(token => token.Start))
        {
            writer.WriteNumberValue(token.Line - line);
            writer.WriteNumberValue(token.Line == line ? token.Start - start : token.Start);
            writer.WriteNumberValue(token.Length);
            writer.WriteNumberValue((int)token.Kind);
            writer.WriteNumberValue((int)token.Modifiers);
            line = token.Line;
            start = token.Start;
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private void ScheduleCheck()
    {
        _checkPending = true;
        _changedAt = Environment.TickCount64;
        Monitor.PulseAll(_gate);
    }

    private void CheckLoop()
    {
        while (NextCheck() is { } documents)
        {
            Publish(Diagnose(documents));
        }
    }

    private IReadOnlyList<OpenDocument>? NextCheck()
    {
        lock (_gate)
        {
            while (true)
            {
                if (_stopping)
                {
                    return null;
                }

                if (!_checkPending)
                {
                    Monitor.Wait(_gate);
                    continue;
                }

                var remaining = _changedAt + (long)_delay.TotalMilliseconds - Environment.TickCount64;
                if (remaining > 0)
                {
                    Monitor.Wait(_gate, TimeSpan.FromMilliseconds(remaining));
                    continue;
                }

                _checkPending = false;
                return [.. _documents.Values];
            }
        }
    }

    private DiagnosticCollector Diagnose(IReadOnlyList<OpenDocument> documents)
    {
        var store = new DocumentSourceStore(documents.ToDictionary(document => document.Path, document => document.Text, SourceLoader.PathComparer));
        var collector = new DiagnosticCollector();
        foreach (var document in documents)
        {
            _uris[document.Path] = document.Uri;
            collector.Include(document.Path);
            var session = new PyropSession(TextReader.Null, new ConsoleProgramOutput(TextWriter.Null, plain: true), store: store);
            try
            {
                session.Check(document.Text, document.Path, document.Path);
            }
            catch (PyropException exception)
            {
                collector.Add(exception, document.Path, session.SourceText);
            }
            catch (Exception exception)
            {
                Log($"Checking {document.Path} failed: {exception.Message}");
            }
        }

        return collector;
    }

    private void Publish(DiagnosticCollector collector)
    {
        foreach (var (path, diagnostics) in collector.Files)
        {
            Publish(path, diagnostics);
        }

        foreach (var path in _published.Where(path => !collector.Files.ContainsKey(path)))
        {
            Publish(path, []);
        }

        _published = collector.Files.Where(file => file.Value.Count > 0).Select(file => file.Key).ToHashSet(SourceLoader.PathComparer);
    }

    private void Publish(string path, IReadOnlyList<Diagnostic> diagnostics)
    {
        var uri = _uris.TryGetValue(path, out var known) ? known : new Uri(path).AbsoluteUri;
        _channel.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WriteString("method", "textDocument/publishDiagnostics");
            writer.WriteStartObject("params");
            writer.WriteString("uri", uri);
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in diagnostics)
            {
                writer.WriteStartObject();
                writer.WriteStartObject("range");
                WritePosition(writer, "start", diagnostic.Line, diagnostic.Start);
                WritePosition(writer, "end", diagnostic.Line, diagnostic.End);
                writer.WriteEndObject();
                writer.WriteNumber("severity", ErrorSeverity);
                writer.WriteString("code", ErrorRenderer.StageName(diagnostic.Stage));
                writer.WriteString("source", "pyrop");
                writer.WriteString("message", diagnostic.Message);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private static void WritePosition(Utf8JsonWriter writer, string name, int line, int character)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("line", line);
        writer.WriteNumber("character", character);
        writer.WriteEndObject();
    }

    private void Log(string text) => _channel.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", "2.0");
        writer.WriteString("method", "window/logMessage");
        writer.WriteStartObject("params");
        writer.WriteNumber("type", ErrorSeverity);
        writer.WriteString("message", text);
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private void WriteResult(JsonElement id, Action<Utf8JsonWriter> writeResult) => _channel.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", "2.0");
        writer.WritePropertyName("id");
        id.WriteTo(writer);
        writer.WritePropertyName("result");
        writeResult(writer);
        writer.WriteEndObject();
    });

    private void WriteError(JsonElement? id, int code, string text) => _channel.Write(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", "2.0");
        writer.WritePropertyName("id");
        if (id is { } requestId)
        {
            requestId.WriteTo(writer);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteStartObject("error");
        writer.WriteNumber("code", code);
        writer.WriteString("message", text);
        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static string? PathOf(string? uri)
    {
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
        {
            return null;
        }

        var path = Uri.UnescapeDataString(parsed.AbsolutePath);
        if (OperatingSystem.IsWindows() && path.Length >= 3 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':')
        {
            path = path[1..];
        }

        if (parsed.Host.Length > 0)
        {
            path = $"//{parsed.Host}{path}";
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static string? LastFullText(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("contentChanges", out var changes)
            || changes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return changes.EnumerateArray()
            .Where(change => change.ValueKind == JsonValueKind.Object && !change.TryGetProperty("range", out _))
            .Select(change => StringAt(change, "text"))
            .LastOrDefault(text => text is not null);
    }

    private static int? NumberAt(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number) && number >= 0 ? number : null;
    }

    private static string? StringAt(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
            {
                return null;
            }
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
