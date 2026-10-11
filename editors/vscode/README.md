# Pyrop for VS Code

Syntax highlighting for [Pyrop](../../Reference.md) (`.prp`) files.

## What it does

- Highlights keywords, types, built-in operations, `true`/`false`, numbers, strings, escape sequences and `#` comments.
- Highlights the names being declared in `function` headers (function name and parameters) and in `def`.
- `Ctrl+/` toggles `#` comments, `"` auto-closes, `block` … `end` regions fold, and the line after `block` is indented.

Calls to user-defined functions look the same as variables. Without brackets, telling them apart needs the interpreter's function table, which a regex grammar cannot see.

## Trying it out

Open this folder (`editors/vscode`) in VS Code and press `F5`. A second VS Code window opens with the extension loaded; open [samples/sample.prp](samples/sample.prp) there. After editing the grammar, run **Developer: Reload Window** in that window to see the changes.

## Installing it

Build a `.vsix` package with the bundled script. It needs no Node.js:

```
powershell -ExecutionPolicy Bypass -File editors\vscode\pack.ps1
```

This creates `editors\vscode\pyrop-0.1.0.vsix`. Install it in either of two ways:

- In VS Code, open the Extensions view, click **…** at the top, and choose **Install from VSIX…**.
- From a terminal: `code --install-extension editors\vscode\pyrop-0.1.0.vsix`

The same `.vsix` file can be sent to anyone else who uses VS Code. After changing the grammar, bump `version` in `package.json`, run the script again, and reinstall.

With Node.js installed, `npx @vscode/vsce package` produces an equivalent package and also checks the manifest.

## Keeping it in sync

The word lists in [syntaxes/pyrop.tmLanguage.json](syntaxes/pyrop.tmLanguage.json) must match `Parsing/Keywords.cs` and `Runtime/Builtins.cs`. Update both whenever a keyword or built-in is added.
