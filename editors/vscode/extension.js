'use strict';

const vscode = require('vscode');
const { spawn } = require('child_process');

const tokenTypes = ['namespace', 'type', 'enumMember', 'function', 'parameter', 'variable', 'property'];
const tokenModifiers = ['defaultLibrary', 'imported'];
const legend = new vscode.SemanticTokensLegend(tokenTypes, tokenModifiers);

let output;
let diagnostics;
let server;
let restarting = Promise.resolve();
let refreshTimer;
const tokensChanged = new vscode.EventEmitter();

class LanguageServer {
    constructor(command) {
        this.command = command;
        this.buffer = Buffer.alloc(0);
        this.nextId = 1;
        this.pending = new Map();
        this.ready = false;
        this.stopping = false;
        this.process = spawn(command, ['--lsp'], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true });
        this.process.stdout.on('data', chunk => this.receive(chunk));
        this.process.stderr.on('data', chunk => output.append(chunk.toString()));
        this.process.stdin.on('error', () => {});
        this.process.on('error', error => this.failed(error));
        this.process.on('exit', (code, signal) => this.exited(code, signal));
        this.request('initialize', {
            processId: process.pid,
            clientInfo: { name: 'Pyrop for VS Code' },
            rootUri: vscode.workspace.workspaceFolders ? vscode.workspace.workspaceFolders[0].uri.toString() : null,
            capabilities: { textDocument: { hover: { contentFormat: ['markdown'] } } }
        }).then(() => {
            this.ready = true;
            this.notify('initialized', {});
            vscode.workspace.textDocuments.forEach(document => this.open(document));
            refreshTokens();
        }, () => {});
    }

    receive(chunk) {
        this.buffer = Buffer.concat([this.buffer, chunk]);
        while (true) {
            const headerEnd = this.buffer.indexOf('\r\n\r\n');
            if (headerEnd < 0) {
                return;
            }

            const header = this.buffer.subarray(0, headerEnd).toString('ascii');
            const match = /Content-Length:\s*(\d+)/i.exec(header);
            const start = headerEnd + 4;
            const length = match ? Number(match[1]) : 0;
            if (this.buffer.length < start + length) {
                return;
            }

            const body = this.buffer.subarray(start, start + length).toString('utf8');
            this.buffer = this.buffer.subarray(start + length);
            let message;
            try {
                message = JSON.parse(body);
            } catch {
                continue;
            }

            this.dispatch(message);
        }
    }

    dispatch(message) {
        if (message.id !== undefined && message.method === undefined) {
            const waiting = this.pending.get(message.id);
            if (waiting) {
                this.pending.delete(message.id);
                if (message.error) {
                    waiting.reject(new Error(message.error.message));
                } else {
                    waiting.resolve(message.result);
                }
            }
        } else if (message.method === 'textDocument/publishDiagnostics') {
            showDiagnostics(message.params);
        } else if (message.method === 'window/logMessage') {
            output.appendLine(message.params.message);
        }
    }

    send(message) {
        const body = Buffer.from(JSON.stringify(Object.assign({ jsonrpc: '2.0' }, message)), 'utf8');
        this.process.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
        this.process.stdin.write(body);
    }

    request(method, params) {
        if (this.process.exitCode !== null || this.process.signalCode !== null || (this.stopping && method !== 'shutdown')) {
            return Promise.reject(new Error('The Pyrop language server is not running.'));
        }

        const id = this.nextId++;
        return new Promise((resolve, reject) => {
            this.pending.set(id, { resolve, reject });
            this.send({ id, method, params });
        });
    }

    notify(method, params) {
        this.send({ method, params });
    }

    open(document) {
        if (this.ready && isPyropFile(document)) {
            this.notify('textDocument/didOpen', {
                textDocument: { uri: document.uri.toString(), languageId: 'pyrop', version: document.version, text: document.getText() }
            });
        }
    }

    change(document) {
        if (this.ready && isPyropFile(document)) {
            this.notify('textDocument/didChange', {
                textDocument: { uri: document.uri.toString(), version: document.version },
                contentChanges: [{ text: document.getText() }]
            });
        }
    }

    close(document) {
        if (this.ready && isPyropFile(document)) {
            this.notify('textDocument/didClose', { textDocument: { uri: document.uri.toString() } });
        }
    }

    filesChanged() {
        if (this.ready) {
            this.notify('workspace/didChangeWatchedFiles', { changes: [] });
        }
    }

    async semanticTokens(document) {
        if (!this.ready || !isPyropFile(document)) {
            return null;
        }

        return this.request('textDocument/semanticTokens/full', { textDocument: { uri: document.uri.toString() } });
    }

    async hover(document, position) {
        if (!this.ready || !isPyropFile(document)) {
            return null;
        }

        return this.request('textDocument/hover', {
            textDocument: { uri: document.uri.toString() },
            position: { line: position.line, character: position.character }
        });
    }

    async definition(document, position) {
        if (!this.ready || !isPyropFile(document)) {
            return null;
        }

        return this.request('textDocument/definition', {
            textDocument: { uri: document.uri.toString() },
            position: { line: position.line, character: position.character }
        });
    }

    async completion(document, position, triggerKind) {
        if (!this.ready || !isPyropFile(document)) {
            return null;
        }

        return this.request('textDocument/completion', {
            textDocument: { uri: document.uri.toString() },
            position: { line: position.line, character: position.character },
            context: { triggerKind }
        });
    }

    async format(document) {
        if (!this.ready || !isPyropFile(document)) {
            return [];
        }

        return this.request('textDocument/formatting', {
            textDocument: { uri: document.uri.toString() },
            options: { tabSize: 4, insertSpaces: true }
        });
    }

    stop() {
        if (this.stopping) {
            return Promise.resolve();
        }

        this.stopping = true;
        return new Promise(resolve => {
            const timer = setTimeout(() => {
                this.process.kill();
                resolve();
            }, 2000);
            this.process.once('exit', () => {
                clearTimeout(timer);
                resolve();
            });
            if (this.process.exitCode !== null || this.process.killed) {
                clearTimeout(timer);
                resolve();
                return;
            }

            this.request('shutdown', null).then(() => this.notify('exit', null), () => this.process.kill());
        });
    }

    failed(error) {
        if (this.stopping) {
            return;
        }

        this.stopping = true;
        this.rejectPending();
        const notFound = error.code === 'ENOENT';
        const message = notFound
            ? `Pyrop: '${this.command}' was not found, so errors are not shown while you type. Install Pyrop or set pyrop.path.`
            : `Pyrop: the language server could not be started: ${error.message}`;
        vscode.window.showWarningMessage(message, 'Open Settings').then(choice => {
            if (choice === 'Open Settings') {
                vscode.commands.executeCommand('workbench.action.openSettings', 'pyrop.');
            }
        });
    }

    exited(code, signal) {
        const wasReady = this.ready;
        this.ready = false;
        this.rejectPending();
        if (this.stopping) {
            return;
        }

        this.stopping = true;
        diagnostics.clear();
        const reason = code === null ? `signal ${signal}` : `exit code ${code}`;
        const message = wasReady
            ? `Pyrop: the language server stopped (${reason}).`
            : `Pyrop: '${this.command} --lsp' did not start a language server. Pyrop 0.11.0 or later is needed.`;
        vscode.window.showWarningMessage(message, 'Restart', 'Open Settings').then(choice => {
            if (choice === 'Restart') {
                restart();
            } else if (choice === 'Open Settings') {
                vscode.commands.executeCommand('workbench.action.openSettings', 'pyrop.');
            }
        });
    }

    rejectPending() {
        for (const waiting of this.pending.values()) {
            waiting.reject(new Error('The Pyrop language server is not running.'));
        }

        this.pending.clear();
    }
}

function isPyropFile(document) {
    return document.languageId === 'pyrop' && document.uri.scheme === 'file';
}

function showDiagnostics(params) {
    const uri = vscode.Uri.parse(params.uri);
    diagnostics.set(uri, params.diagnostics.map(item => {
        const range = new vscode.Range(item.range.start.line, item.range.start.character, item.range.end.line, item.range.end.character);
        const diagnostic = new vscode.Diagnostic(range, item.message, vscode.DiagnosticSeverity.Error);
        diagnostic.source = item.source;
        diagnostic.code = item.code;
        return diagnostic;
    }));
    refreshTokens();
}

function toRange(range) {
    return new vscode.Range(range.start.line, range.start.character, range.end.line, range.end.character);
}

function toCompletionItem(entry) {
    const label = entry.labelDetails ? { label: entry.label, description: entry.labelDetails.description } : entry.label;
    const item = new vscode.CompletionItem(label, entry.kind - 1);
    item.sortText = entry.sortText;
    item.detail = entry.detail;
    if (entry.documentation) {
        item.documentation = new vscode.MarkdownString(entry.documentation.value);
    }

    return item;
}

function start() {
    const configuration = vscode.workspace.getConfiguration('pyrop');
    if (!configuration.get('languageServer.enabled', true)) {
        return;
    }

    server = new LanguageServer(configuration.get('path') || 'pyrop');
}

async function stop() {
    const running = server;
    server = undefined;
    if (diagnostics) {
        diagnostics.clear();
    }

    if (running) {
        await running.stop();
    }
}

function restart() {
    restarting = restarting.then(async () => {
        await stop();
        start();
        refreshTokens();
    });
    return restarting;
}

function refreshTokens() {
    if (refreshTimer === undefined) {
        refreshTimer = setTimeout(() => {
            refreshTimer = undefined;
            tokensChanged.fire();
        }, 100);
    }
}

function activate(context) {
    output = vscode.window.createOutputChannel('Pyrop');
    diagnostics = vscode.languages.createDiagnosticCollection('pyrop');
    const watcher = vscode.workspace.createFileSystemWatcher('**/*.prp');
    const filesChanged = () => server && server.filesChanged();
    context.subscriptions.push(
        output,
        diagnostics,
        watcher,
        tokensChanged,
        watcher.onDidCreate(filesChanged),
        watcher.onDidChange(filesChanged),
        watcher.onDidDelete(filesChanged),
        vscode.commands.registerCommand('pyrop.restartLanguageServer', restart),
        vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration('pyrop.path') || event.affectsConfiguration('pyrop.languageServer.enabled')) {
                restart();
            }
        }),
        vscode.workspace.onDidOpenTextDocument(document => server && server.open(document)),
        vscode.workspace.onDidChangeTextDocument(event => server && server.change(event.document)),
        vscode.workspace.onDidCloseTextDocument(document => server && server.close(document)),
        vscode.languages.registerDocumentSemanticTokensProvider({ language: 'pyrop', scheme: 'file' }, {
            onDidChangeSemanticTokens: tokensChanged.event,
            async provideDocumentSemanticTokens(document) {
                let result = null;
                try {
                    result = server ? await server.semanticTokens(document) : null;
                } catch {
                    result = null;
                }

                if (!result) {
                    throw new vscode.CancellationError();
                }

                return new vscode.SemanticTokens(new Uint32Array(result.data));
            }
        }, legend),
        vscode.languages.registerHoverProvider({ language: 'pyrop', scheme: 'file' }, {
            async provideHover(document, position) {
                let result = null;
                try {
                    result = server ? await server.hover(document, position) : null;
                } catch {
                    result = null;
                }

                if (!result || !result.contents) {
                    return null;
                }

                return new vscode.Hover(new vscode.MarkdownString(result.contents.value), result.range ? toRange(result.range) : undefined);
            }
        }),
        vscode.languages.registerDefinitionProvider({ language: 'pyrop', scheme: 'file' }, {
            async provideDefinition(document, position) {
                let result = null;
                try {
                    result = server ? await server.definition(document, position) : null;
                } catch {
                    result = null;
                }

                return result ? new vscode.Location(vscode.Uri.parse(result.uri), toRange(result.range)) : null;
            }
        }),
        vscode.languages.registerCompletionItemProvider({ language: 'pyrop', scheme: 'file' }, {
            async provideCompletionItems(document, position, token, context) {
                let result = null;
                try {
                    result = server ? await server.completion(document, position, context.triggerKind + 1) : null;
                } catch {
                    result = null;
                }

                return result ? result.items.map(toCompletionItem) : [];
            }
        }, ' '),
        vscode.languages.registerDocumentFormattingEditProvider({ language: 'pyrop', scheme: 'file' }, {
            async provideDocumentFormattingEdits(document) {
                let edits = [];
                try {
                    edits = server ? await server.format(document) : [];
                } catch {
                    edits = [];
                }

                return (edits || []).map(edit => vscode.TextEdit.replace(toRange(edit.range), edit.newText));
            }
        }));
    start();
}

function deactivate() {
    restarting = restarting.then(stop);
    return restarting;
}

module.exports = { activate, deactivate };
