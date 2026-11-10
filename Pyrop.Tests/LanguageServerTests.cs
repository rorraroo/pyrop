using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Pyrop.LanguageServer;

namespace Pyrop.Tests;

public sealed class LanguageServerTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _directory = Directory.CreateTempSubdirectory("pyrop-lsp-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class Client : IDisposable
    {
        private readonly AnonymousPipeServerStream _toServer = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _fromServer = new(PipeDirection.In);
        private readonly JsonRpcChannel _channel;
        private int _nextId;

        public Client(TimeSpan delay)
        {
            var input = new AnonymousPipeClientStream(PipeDirection.In, _toServer.ClientSafePipeHandle);
            var output = new AnonymousPipeClientStream(PipeDirection.Out, _fromServer.ClientSafePipeHandle);
            Server = Task.Run(() =>
            {
                using (input)
                using (output)
                {
                    return PyropLanguageServer.Run(input, output, delay);
                }
            });
            _channel = new JsonRpcChannel(_fromServer, _toServer);
        }

        public Task<int> Server { get; }

        public void SendRaw(string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            _toServer.Write(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"));
            _toServer.Write(bytes);
            _toServer.Flush();
        }

        public void SendRawHeader(string header)
        {
            _toServer.Write(Encoding.ASCII.GetBytes(header));
            _toServer.Flush();
        }

        public int Request(string method, object? parameters = null)
        {
            var id = ++_nextId;
            SendRaw(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
            return id;
        }

        public void Notify(string method, object? parameters = null) =>
            SendRaw(JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params = parameters }));

        public JsonElement Next()
        {
            var read = Task.Run(_channel.Read);
            Assert.True(read.Wait(Patience), "The server sent nothing.");
            Assert.NotNull(read.Result);
            return JsonDocument.Parse(read.Result).RootElement.Clone();
        }

        public JsonElement Response(int id)
        {
            var message = Next();
            Assert.Equal(id, message.GetProperty("id").GetInt32());
            return message;
        }

        public JsonElement Initialize()
        {
            var result = Response(Request("initialize", new { capabilities = new { } })).GetProperty("result");
            Notify("initialized", new { });
            return result;
        }

        public void Open(string uri, string text) =>
            Notify("textDocument/didOpen", new { textDocument = new { uri, languageId = "pyrop", version = 1, text } });

        public void Change(string uri, string text) =>
            Notify("textDocument/didChange", new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text } } });

        public (string Uri, JsonElement Diagnostics) Diagnostics()
        {
            var message = Next();
            Assert.Equal("textDocument/publishDiagnostics", message.GetProperty("method").GetString());
            var parameters = message.GetProperty("params");
            return (parameters.GetProperty("uri").GetString()!, parameters.GetProperty("diagnostics"));
        }

        public int Exit()
        {
            Assert.True(Server.Wait(Patience), "The server did not exit.");
            return Server.Result;
        }

        public void Dispose()
        {
            _toServer.Dispose();
            Server.Wait(Patience);
            _fromServer.Dispose();
        }
    }

    private string FileOnDisk(string name, string text)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    private string UriOf(string name) => new Uri(Path.Combine(_directory, name)).AbsoluteUri;

    private string VsCodeUriOf(string name)
    {
        var path = Path.Combine(_directory, name).Replace('\\', '/');
        return OperatingSystem.IsWindows()
            ? $"file:///{char.ToLowerInvariant(path[0])}%3A{Uri.EscapeDataString(path[2..]).Replace("%2F", "/")}"
            : new Uri(path).AbsoluteUri;
    }

    private static Client Started(TimeSpan? delay = null)
    {
        var client = new Client(delay ?? TimeSpan.Zero);
        client.Initialize();
        return client;
    }

    [Fact]
    public void InitializeAnnouncesFullTextSyncAndSemanticTokens()
    {
        using var client = new Client(TimeSpan.Zero);

        var result = client.Initialize();

        var capabilities = result.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("textDocumentSync").GetProperty("openClose").GetBoolean());
        Assert.Equal(1, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32());
        var legend = capabilities.GetProperty("semanticTokensProvider").GetProperty("legend");
        Assert.Equal(SemanticClassifier.KindNames, legend.GetProperty("tokenTypes").EnumerateArray().Select(type => type.GetString()));
        Assert.Equal(SemanticClassifier.ModifierNames, legend.GetProperty("tokenModifiers").EnumerateArray().Select(type => type.GetString()));
        Assert.Equal("pyrop", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(capabilities.GetProperty("hoverProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("documentFormattingProvider").GetBoolean());
    }

    [Fact]
    public void HoverShowsTheGroupingOfTheCallUnderTheCursor()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "def int x 2\nwrite_line add 1 multiply 2 x");
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/hover", new { textDocument = new { uri }, position = new { line = 1, character = 12 } }))
            .GetProperty("result");

        Assert.Equal("markdown", result.GetProperty("contents").GetProperty("kind").GetString());
        Assert.StartsWith("```pyrop\nadd 1 (multiply 2 x)\n```\n\nGives `int`.", result.GetProperty("contents").GetProperty("value").GetString());
        var range = result.GetProperty("range");
        Assert.Equal((1, 11), (range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32()));
        Assert.Equal((1, 14), (range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32()));
    }

    [Fact]
    public void HoverInDeeplyNestedCodeDoesNotOverflowTheStack()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, string.Concat(Enumerable.Repeat("if true\n", 900)) + "write_line 1\n");
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/hover", new { textDocument = new { uri }, position = new { line = 900, character = 0 } }))
            .GetProperty("result");

        Assert.Equal("```pyrop\nwrite_line 1\n```", result.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void InitializeAnnouncesDefinitionAndCompletion()
    {
        using var client = new Client(TimeSpan.Zero);

        var capabilities = client.Initialize().GetProperty("capabilities");

        Assert.True(capabilities.GetProperty("definitionProvider").GetBoolean());
        Assert.Equal(" ", Assert.Single(capabilities.GetProperty("completionProvider").GetProperty("triggerCharacters").EnumerateArray()).GetString());
    }

    [Fact]
    public void DefinitionGivesTheLocationInAnOpenModuleUnderTheClientsUri()
    {
        using var client = Started();
        var main = VsCodeUriOf("main.prp");
        var library = VsCodeUriOf("lib.prp");
        client.Open(library, "export function int one\n    return 1\n");
        client.Diagnostics();
        client.Open(main, "use lib \"lib.prp\"\nwrite_line from lib one\n");
        client.Diagnostics();
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/definition", new { textDocument = new { uri = main }, position = new { line = 1, character = 21 } }))
            .GetProperty("result");

        Assert.Equal(library, result.GetProperty("uri").GetString());
        var start = result.GetProperty("range").GetProperty("start");
        Assert.Equal((0, 20), (start.GetProperty("line").GetInt32(), start.GetProperty("character").GetInt32()));
        Assert.Equal(23, result.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void DefinitionOfAWordWithoutOneIsNull()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "write_line 1\n");
        client.Diagnostics();

        var response = client.Response(client.Request("textDocument/definition", new { textDocument = new { uri }, position = new { line = 0, character = 2 } }));

        Assert.Equal(JsonValueKind.Null, response.GetProperty("result").ValueKind);
    }

    [Fact]
    public void CompletionItemsCarryArityAndSignature()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "function int square int x\n    return x\n\nwrite_line squ\n");
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/completion", new { textDocument = new { uri }, position = new { line = 3, character = 14 }, context = new { triggerKind = 1 } }))
            .GetProperty("result");

        Assert.False(result.GetProperty("isIncomplete").GetBoolean());
        var item = Assert.Single(result.GetProperty("items").EnumerateArray());
        Assert.Equal("square", item.GetProperty("label").GetString());
        Assert.Equal(3, item.GetProperty("kind").GetInt32());
        Assert.Equal("takes 1", item.GetProperty("labelDetails").GetProperty("description").GetString());
        Assert.Equal("function int square int x", item.GetProperty("detail").GetString());
    }

    [Fact]
    public void ASpaceInGeneralCodeCompletesNothing()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "write_line \n");
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/completion", new { textDocument = new { uri }, position = new { line = 0, character = 11 }, context = new { triggerKind = 2, triggerCharacter = " " } }))
            .GetProperty("result");

        Assert.Empty(result.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void CompletionWithADeeplyNestedTypeDoesNotOverflowTheStack()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, $"function void f {string.Concat(Enumerable.Repeat("list ", 60000))}int x\n    write_line 1\n");
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/completion", new { textDocument = new { uri }, position = new { line = 1, character = 4 }, context = new { triggerKind = 1 } }))
            .GetProperty("result");

        Assert.NotEmpty(result.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public void EveryParseErrorInADocumentIsADiagnostic()
    {
        using var client = Started();
        var uri = UriOf("main.prp");

        client.Open(uri, "function void a\n    write_line 1 2\n\nfunction void b\n    write_line 3 4\n");

        Assert.Equal(2, client.Diagnostics().Diagnostics.GetArrayLength());
    }

    [Fact]
    public void TheFormattingRangeCountsEveryKindOfLineEnd()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "if true\r\nwrite_line 1\r# a\n");
        client.Diagnostics();

        var edit = Assert.Single(client.Response(client.Request("textDocument/formatting", new { textDocument = new { uri } }))
            .GetProperty("result").EnumerateArray());

        var end = edit.GetProperty("range").GetProperty("end");
        Assert.Equal((3, 0), (end.GetProperty("line").GetInt32(), end.GetProperty("character").GetInt32()));
    }

    [Fact]
    public void HoverOverNothingOrAClosedDocumentIsNull()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "write_line 1");
        client.Diagnostics();

        var space = client.Response(client.Request("textDocument/hover", new { textDocument = new { uri }, position = new { line = 0, character = 10 } }));
        var closed = client.Response(client.Request("textDocument/hover", new { textDocument = new { uri = UriOf("other.prp") }, position = new { line = 0, character = 0 } }));

        Assert.Equal(JsonValueKind.Null, space.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("result").ValueKind);
    }

    [Fact]
    public void FormattingReplacesTheWholeDocument()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "if true\nwrite_line 1\n");
        client.Diagnostics();

        var edits = client.Response(client.Request("textDocument/formatting", new { textDocument = new { uri }, options = new { tabSize = 2, insertSpaces = false } }))
            .GetProperty("result");

        var edit = Assert.Single(edits.EnumerateArray());
        var range = edit.GetProperty("range");
        Assert.Equal((0, 0), (range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32()));
        Assert.Equal((2, 0), (range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32()));
        Assert.Equal("if true\n    write_line 1\n", edit.GetProperty("newText").GetString());
    }

    [Theory]
    [InlineData("if true\n    write_line 1\n")]
    [InlineData("if true\nwrite_line add 1\n")]
    public void FormattingAFormattedOrUnparsableDocumentChangesNothing(string text)
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, text);
        client.Diagnostics();

        var edits = client.Response(client.Request("textDocument/formatting", new { textDocument = new { uri } })).GetProperty("result");

        Assert.Empty(edits.EnumerateArray());
    }

    [Fact]
    public void ATypeErrorIsPublishedWithItsRangeStageAndMessage()
    {
        using var client = Started();
        var uri = UriOf("main.prp");

        client.Open(uri, "write_line 1\ndef int x \"text\"\n");

        var (published, diagnostics) = client.Diagnostics();
        Assert.Equal(uri, published);
        var diagnostic = Assert.Single(diagnostics.EnumerateArray());
        var range = diagnostic.GetProperty("range");
        Assert.Equal(1, range.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(10, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(1, range.GetProperty("end").GetProperty("line").GetInt32());
        Assert.Equal(16, range.GetProperty("end").GetProperty("character").GetInt32());
        Assert.Equal(1, diagnostic.GetProperty("severity").GetInt32());
        Assert.Equal("Type error", diagnostic.GetProperty("code").GetString());
        Assert.Equal("pyrop", diagnostic.GetProperty("source").GetString());
        Assert.Contains("int", diagnostic.GetProperty("message").GetString());
    }

    [Fact]
    public void EveryTypeErrorIsShownButOnlyTheFirstParseError()
    {
        using var client = Started();

        client.Open(UriOf("types.prp"), "def int a \"x\"\ndef bool b 3\n");
        Assert.Equal(2, client.Diagnostics().Diagnostics.GetArrayLength());

        client.Change(UriOf("types.prp"), "write_line add 1\nwrite_line add 2\n");
        var diagnostic = Assert.Single(client.Diagnostics().Diagnostics.EnumerateArray());
        Assert.Equal("Parse error", diagnostic.GetProperty("code").GetString());
    }

    [Fact]
    public void AnErrorAtTheEndOfInputHasAnEmptyRangeThere()
    {
        using var client = Started();

        client.Open(UriOf("main.prp"), "block");

        var range = Assert.Single(client.Diagnostics().Diagnostics.EnumerateArray()).GetProperty("range");
        Assert.Equal(5, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(5, range.GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void FixingTheErrorPublishesAnEmptyList()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "def int x \"text\"");
        Assert.Equal(1, client.Diagnostics().Diagnostics.GetArrayLength());

        client.Change(uri, "def int x 3");

        Assert.Equal(0, client.Diagnostics().Diagnostics.GetArrayLength());
    }

    [Fact]
    public void ChangesWithinTheDelayAreCheckedOnce()
    {
        using var client = Started(TimeSpan.FromSeconds(1));
        var uri = UriOf("main.prp");

        client.Open(uri, "def int x \"text\"");
        client.Change(uri, "def int x 3");

        Assert.Equal(0, client.Diagnostics().Diagnostics.GetArrayLength());
    }

    [Fact]
    public void AnUnsavedModuleIsCheckedAsShown()
    {
        FileOnDisk("lib.prp", "export function int double int n\n    return multiply n 2\n");
        using var client = Started();
        client.Open(VsCodeUriOf("main.prp"), "use lib \"lib.prp\"\nwrite_line from lib double 4\n");
        Assert.Equal(0, client.Diagnostics().Diagnostics.GetArrayLength());

        client.Open(VsCodeUriOf("lib.prp"), "export function int triple int n\n    return multiply n 3\n");

        var published = Enumerable.Range(0, 2).Select(_ => client.Diagnostics()).ToDictionary(item => item.Uri, item => item.Diagnostics);
        Assert.Equal(0, published[VsCodeUriOf("lib.prp")].GetArrayLength());
        var diagnostic = Assert.Single(published[VsCodeUriOf("main.prp")].EnumerateArray());
        Assert.Contains("'double'", diagnostic.GetProperty("message").GetString());
    }

    [Fact]
    public void AnErrorInAModuleIsReportedInTheModuleOnce()
    {
        FileOnDisk("lib.prp", "export function int double int n\n    return \"two\"\n");
        using var client = Started();

        client.Open(UriOf("main.prp"), "use lib \"lib.prp\"\nwrite_line from lib double 4\n");

        var published = Enumerable.Range(0, 2).Select(_ => client.Diagnostics()).ToDictionary(item => item.Uri, item => item.Diagnostics);
        Assert.Equal(0, published[UriOf("main.prp")].GetArrayLength());
        var diagnostic = Assert.Single(published[UriOf("lib.prp")].EnumerateArray());
        Assert.Equal(1, diagnostic.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public void ClosingADocumentClearsItsDiagnostics()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "def int x \"text\"");
        client.Diagnostics();

        client.Notify("textDocument/didClose", new { textDocument = new { uri } });

        var (published, diagnostics) = client.Diagnostics();
        Assert.Equal(uri, published);
        Assert.Equal(0, diagnostics.GetArrayLength());
    }

    [Fact]
    public void DocumentsThatAreNotFilesAreIgnored()
    {
        using var client = Started();

        client.Open("untitled:Untitled-1", "def int x \"text\"");
        client.Open(UriOf("main.prp"), "def int x 3");

        Assert.Equal(UriOf("main.prp"), client.Diagnostics().Uri);
        var id = client.Request("shutdown");
        client.Response(id);
    }

    [Fact]
    public void SemanticTokensClassifyEveryKindOfWord()
    {
        FileOnDisk("lib.prp", "export function int double int n\n    return multiply n 2\n");
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "use lib \"lib.prp\"\nimport from lib double end\nfunction int twice int n return double double n\ndef int x twice 3\n");
        client.Diagnostics();

        var data = client.Response(client.Request("textDocument/semanticTokens/full", new { textDocument = new { uri } }))
            .GetProperty("result").GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();

        var kinds = SemanticClassifier.KindNames;
        var decoded = data.Chunk(5).Select(token => (kinds[token[3]], token[4])).ToList();
        Assert.Equal(
            [("namespace", 0), ("namespace", 0), ("function", 2), ("function", 0), ("parameter", 0), ("function", 2), ("function", 2), ("parameter", 0), ("variable", 0), ("function", 0)],
            decoded);
        Assert.Equal([0, 4, 3], data[..3]);
    }

    [Fact]
    public void SemanticTokensMarkFieldsAfterTheAndWith()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "record P int x\ndef P p P 1\ndef int x the x of p\nset p copy p with x x\n");
        client.Diagnostics();

        Assert.Equal(
            ["type", "property", "type", "variable", "type", "variable", "property", "variable", "variable", "variable", "property", "variable"],
            KindsOf(Tokens(client, uri)));
    }

    [Fact]
    public void SemanticTokensOfAFileThatCannotBeLexedAreNull()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "write_line \"unterminated");
        client.Diagnostics();

        var result = client.Response(client.Request("textDocument/semanticTokens/full", new { textDocument = new { uri } })).GetProperty("result");

        Assert.Equal(JsonValueKind.Null, result.ValueKind);
    }

    [Fact]
    public void UnknownRequestsAndBadMessagesGetErrorsAndTheServerKeepsRunning()
    {
        using var client = new Client(TimeSpan.Zero);

        var early = client.Request("textDocument/references");
        Assert.Equal(-32002, client.Response(early).GetProperty("error").GetProperty("code").GetInt32());

        client.Initialize();
        var unknown = client.Request("textDocument/references");
        Assert.Equal(-32601, client.Response(unknown).GetProperty("error").GetProperty("code").GetInt32());

        client.SendRaw("{ not json");
        var parseError = client.Next();
        Assert.Equal(JsonValueKind.Null, parseError.GetProperty("id").ValueKind);
        Assert.Equal(-32700, parseError.GetProperty("error").GetProperty("code").GetInt32());

        client.Response(client.Request("shutdown"));
        var late = client.Request("textDocument/references");
        Assert.Equal(-32600, client.Response(late).GetProperty("error").GetProperty("code").GetInt32());
    }

    private static int[] Tokens(Client client, string uri) =>
        client.Response(client.Request("textDocument/semanticTokens/full", new { textDocument = new { uri } }))
            .GetProperty("result").GetProperty("data").EnumerateArray().Select(item => item.GetInt32()).ToArray();

    private static List<string> KindsOf(int[] data) =>
        data.Chunk(5).Select(token => SemanticClassifier.KindNames[token[3]]).ToList();

    [Fact]
    public void ALoneSurrogateInAStringDoesNotStopTheServer()
    {
        using var client = Started();

        client.SendRaw(
            """{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":""" +
            $"\"{UriOf("bad.prp")}\"" +
            ""","text":"# \udc00\nwrite_line 1"}}}""");
        client.SendRaw("""{"jsonrpc":"2.0","id":7,"method":"textDocument/semanticTokens/full","params":{"textDocument":{"uri":"file:///\udc00.prp"}}}""");
        Assert.Equal(JsonValueKind.Null, client.Response(7).GetProperty("result").ValueKind);

        client.Open(UriOf("good.prp"), "def int x \"text\"");
        Assert.Equal(UriOf("good.prp"), client.Diagnostics().Uri);
    }

    [Fact]
    public void AnImpossibleContentLengthEndsTheServerInsteadOfExhaustingMemory()
    {
        using var client = Started();

        client.SendRawHeader("Content-Length: 2147483647\r\n\r\n");

        Assert.Equal(1, client.Exit());
    }

    [Fact]
    public void AChangedFileOnDiskIsCheckedAgain()
    {
        FileOnDisk("lib.prp", "export function int double int n\n    return multiply n 2\n");
        using var client = Started();
        client.Open(UriOf("main.prp"), "use lib \"lib.prp\"\nwrite_line from lib double 4\n");
        Assert.Equal(0, client.Diagnostics().Diagnostics.GetArrayLength());

        FileOnDisk("lib.prp", "export function int triple int n\n    return multiply n 3\n");
        client.Notify("workspace/didChangeWatchedFiles", new { changes = new[] { new { uri = UriOf("lib.prp"), type = 2 } } });

        Assert.Equal(1, client.Diagnostics().Diagnostics.GetArrayLength());
    }

    [Theory]
    [InlineData("def string s \"x\"\r\ndef int x \"a\"\r\n", 1, 10, 13)]
    [InlineData("def string s \"\U0001F600\" def int x \"a\"\n", 0, 28, 31)]
    public void RangesCountUtf16AndIgnoreCarriageReturns(string text, int line, int start, int end)
    {
        using var client = Started();

        client.Open(UriOf("main.prp"), text);

        var range = Assert.Single(client.Diagnostics().Diagnostics.EnumerateArray()).GetProperty("range");
        Assert.Equal(line, range.GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(start, range.GetProperty("start").GetProperty("character").GetInt32());
        Assert.Equal(end, range.GetProperty("end").GetProperty("character").GetInt32());
    }

    [Fact]
    public void AModuleThatDoesNotParseKeepsTheTokensOfTheFileUsingIt()
    {
        FileOnDisk("lib.prp", "end\n");
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "use lib \"lib.prp\"\ndef int x 1\n");
        var published = new[] { client.Diagnostics(), client.Diagnostics() }.ToDictionary(item => item.Uri, item => item.Diagnostics);
        Assert.Equal(1, published[UriOf("lib.prp")].GetArrayLength());

        Assert.Equal(["namespace", "variable"], KindsOf(Tokens(client, uri)));
    }

    [Fact]
    public void AParseErrorInsideAFunctionEndsItsParametersAtTheNextTopLevelLine()
    {
        using var client = Started();
        var uri = UriOf("main.prp");
        client.Open(uri, "function int g int a\n    return add a\ndef int a 3\nwrite_line a\n");
        client.Diagnostics();

        Assert.Equal(["function", "parameter", "function", "parameter", "variable", "variable"], KindsOf(Tokens(client, uri)));
    }

    [Fact]
    public void ExitAfterShutdownEndsWithZero()
    {
        using var client = Started();
        client.Response(client.Request("shutdown"));

        client.Notify("exit");

        Assert.Equal(0, client.Exit());
    }

    [Fact]
    public void ExitWithoutShutdownEndsWithOne()
    {
        using var client = Started();

        client.Notify("exit");

        Assert.Equal(1, client.Exit());
    }
}
