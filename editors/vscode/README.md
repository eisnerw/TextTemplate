# TextTemplate for VS Code

This extension provides basic TextTemplate language support for `.template`
files. It associates the file type, applies lexical syntax coloring, and starts
the C# language server to report parser errors in open and edited documents.

The language server reuses `TextTemplate.Compiler` and parses the unsaved buffer
provided by VS Code. Files beneath a workspace `subtemplates/` directory use the
compiler's shared-template parsing mode. Diagnostics appear as editor markers
and in the Problems panel.

The initial version does not provide subtemplate navigation, completions,
runtime-value hovers, rendering, or generated-C# source mapping. Coloring is a
lightweight TextMate grammar; parser-based semantic coloring is not included.

## Requirements

- VS Code 1.90 or later
- Node.js 20 or later and npm
- .NET 10 SDK to build the language server
- .NET 10 runtime to run the installed extension

## Build and run from source

From this directory (`editors/vscode`):

```sh
npm ci
npm run compile
```

Open this directory in VS Code and press **F5**. The extension development host
will load the extension and launch the language server over standard input and
output. Open a `.template` file; syntax errors are reported as you edit.

The C# language server project is at
`src/TextTemplate.LanguageServer` and references the handwritten parser in
`src/TextTemplate.Compiler`.

## Package and install

Build a VS Code extension package:

```sh
npm ci
npm run package:vsix
```

This publishes the framework-dependent language server into `server/dist/`
inside the package. Install the generated `.vsix` from the VS Code Extensions
view using **Install from VSIX...**. The installed machine needs the .NET 10
runtime available as `dotnet` on `PATH`.

To install from a terminal when the `code` command is available:

```sh
code --install-extension texttemplate-0.0.1.vsix
```

The current publisher is a local-development placeholder. Set the intended
publisher before publishing to the Visual Studio Marketplace.

## MVP boundary

The language server supports initialization, full-document synchronization,
syntax diagnostics, and clearing diagnostics when a document closes. It does
not execute templates. A later Preview command can invoke generation and run the
generated C# separately from this editor-language service.

Run the server protocol smoke test with:

```sh
npm run test:server
```