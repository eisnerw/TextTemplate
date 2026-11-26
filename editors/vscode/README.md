# TextTemplate for VS Code

This extension provides basic TextTemplate language support for `.template`
files. It associates the file type, applies lexical syntax coloring, and starts
the C# language server to report parser errors in open and edited documents.
F12 navigates from a subtemplate reference to its local definition or shared
subtemplate file.

The language server reuses `TextTemplate.Compiler` and parses the unsaved buffer
provided by VS Code. It detects a template root by finding sibling `templates/`
and `subtemplates/` directories under the workspace, including nested layouts
such as `reference/templates/`. Diagnostics appear as editor markers and in the
Problems panel.

F12 resolves local definitions in the current file before matching shared files
by filename. Nested local scopes are searched from the nearest scope outward.
When multiple definitions remain possible, VS Code receives all candidate
locations rather than an arbitrary choice. Open buffers override on-disk text
for navigation. This initial index is built when F12 is requested.

Unresolved references do not yet produce diagnostics. The index is a foundation
for that check, but dynamic includes and runtime-loaded resources require extra
handling. Method names, including built-ins and registered extensions, are not
yet semantically validated or distinguished by color. Coloring is a lightweight
TextMate grammar. Completions, runtime-value hovers, rendering, and generated-C#
source mapping are not included.

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
code --install-extension texttemplate-0.0.2.vsix
```

To refresh an earlier local installation, package again and install the new
`texttemplate-0.0.2.vsix` from the Extensions view. Reload the VS Code window
when prompted.

The current publisher is a local-development placeholder. Set the intended
publisher before publishing to the Visual Studio Marketplace.

## MVP boundary

The language server supports initialization, full-document synchronization,
syntax diagnostics, clearing diagnostics when a document closes, and F12
definition requests. It does not execute templates. A later Preview command can
invoke generation and run the generated C# separately from this editor-language
service.

Run the server protocol smoke test with:

```sh
npm run test:server
```