# TextTemplate compiler

The compiler parses an entire root directory and generates standalone C# by
default. Parse-tree JSON is an optional export. The handwritten C# parser uses
the unchanged grammar and needs neither ANTLR nor Node.js at runtime.

## Generate and run

From the repository root:

```sh
dotnet build src/TextTemplate.Compiler
mkdir -p /tmp/texttemplate-example/templates /tmp/texttemplate-example/subtemplates
printf 'Hello {firstName}!' > /tmp/texttemplate-example/templates/hello.template
# Emit an editable standalone project (default destination: Generated/):
dotnet run --no-build --project src/TextTemplate.Compiler -- --root /tmp/texttemplate-example
# Build and run without referencing any TextTemplate assembly:
dotnet build Generated/GeneratedTemplates.csproj
printf '{"firstName":"Ada"}' > /tmp/texttemplate-input.json
dotnet run --no-build --project Generated -- --template hello --data-file /tmp/texttemplate-input.json
```

Generation currently implements a subset of execution semantics. The complete
reference collection compiles, but templates that reach unfinished features throw
`NotImplementedException`. Some methods/annotations, inline calls, and HTTP
resolution still have stubs. Supported additions include local/shared `@Include` with computed names,
literal `@MissingValue`, named subtemplate methods with scalar arguments, and
list contexts and per-item inline/named evaluation, and `Join` with an optional
final separator (scalar input remains unchanged). String methods now include
`Trim`, `StartsWith`, `EndsWith`, `Contains`, string-search `Replace`, `Substr`,
`IndexOf`, `LastIndexOf`, and `EndIndexOf`. These preserve JavaScript whitespace,
UTF-16 indexing, `parseInt` substring offsets, and split/join replacement behavior.
`ToUpper` and `ToLower` use frozen Node 24.22.0 / Unicode 17.0 casing tables,
including expansions and contextual final sigma. `GreaterThan` and `LessThan`
share the interpreter's integer-prefix numeric comparison and ordinal UTF-16
string comparison with relational operators. Regex literals support the frozen grammar's `g`, `i`, `m`, and `s` flags in
`Replace`, `Matches`, `Assert`, and scoped `@Falsy` (including clearing with
`@Falsy()`). Regex replacement follows JavaScript capture/substitution rules;
string replacement retains its existing split/join behavior. `Contains`,
`StartsWith`, and `EndsWith` reject regex arguments, as Node does.
The emitted `Support/TemplateRegex.cs` adapts .NET matching with JavaScript
character classes, anchors, non-Unicode case folding, and global state. Repeated
groups containing nested captures, forward/octal backreferences, Unicode capture
names, duplicate capture names, and some legacy character-class/control escapes
remain source-located runtime stubs. `@DateTest` remains unfinished along with date
parsing/formatting. No grammar changes or runtime Node dependency are introduced. Exceptions from
emitted template logic identify the feature and original file, line, and column.
Stubs do not run their operands; an unselected conditional branch does not throw.
These are temporary implementation points, not language restrictions or successful
renderings. Full reference-output parity remains future work.

Implemented behavior has focused A/B coverage for literals, scalar lookups,
missing values, whitespace, predicates, arrows, object contexts, and local/shared
subtemplates. The generated source contains template-specific C# methods and
helper source, with no runtime parser, syntax tree, or TextTemplate DLL dependency.
The public API is `GeneratedTemplates.Render(name, JsonElement? data = null)`.
Each generated template exposes an internal `Evaluate()` method that returns
structured content; the public API composes that content into the final string.
Built-in language methods use `Builtins.Join(...)`, with their entry points in
`Support/Builtins.cs`; user-defined template methods retain their own names.
Generated lookups use `IsTruthy(path)` for legacy truth checks and
`MethodInput(path)` to collect a method input across current item contexts.
Complex method inputs use the deferred `MethodInput(itemContext => ...)` overload.
The executable accepts `--template NAME`, optional `--data-file FILE`, and
`--data-root DIRECTORY`; rendered
text goes exactly to stdout, and execution errors go to stderr with exit code 1.

For debugging, runtime data, evaluation contexts, missing values, and structured
text expose `ToString()` summaries and `DebuggerDisplay` attributes. Data summaries
show JSON-like stored values without evaluating templates or traversing parents.
Previews limit nesting to four levels, collections to eight entries, and strings
to 160 characters; expand `Values` and `Parent` in the debugger for full details.
These diagnostic summaries are separate from template rendering.

The original named data collection has also been expanded into
`reference/data/*.json`. Pass any one of these files with the existing
`--data-file` option:

```sh
dotnet run --no-build --project Generated -- \
  --template simpledata --data-file reference/data/nested.json
```

Each file contains an input object or array, not the map of all named datasets.
Data paths are relative to the current working directory, and template and data
names need not match. Data is loaded at execution time, so changing files requires
no regeneration. No data file is inferred automatically. The example above now renders:

```text
Hello Smith, John with a dog named missing --namex-- THIS IS AN INCLUDED SUBTEMPLATE!!
```

The singleton pet array collapses to an object as in the interpreter. Multiple pets
also work, including the three-pet fixture:

```sh
dotnet run --project Generated -- --template simpledata --data-file reference/data/simpledata.json
```

List items inherit definitions and annotations, retain parent links, and preserve
structured content until composition. `Join(', ', ' and ')` uses the final separator
between the last two retained items. Filtering, grouping, and general
object formatting remain unfinished.

## Compiler options

```sh
# Default: all templates and subtemplates as standalone C#
dotnet run --no-build --project src/TextTemplate.Compiler -- --root reference --output Generated
# Restrict generation to a named template and its discovered shared dependencies
dotnet run --no-build --project src/TextTemplate.Compiler -- --root reference --template simple --output /tmp/Simple.cs
# Optional comprehensive parse-tree map
dotnet run --no-build --project src/TextTemplate.Compiler -- --root reference --format json --output /tmp/texttemplate-trees.json
# Omit --output for Generated/ (C#) or stdout (JSON)
# An explicit .cs output path writes a combined standalone source file
# --format csharp explicitly selects the default; --help lists usage
```

The root must contain both `templates/` and `subtemplates/`, scanned recursively
for UTF-8 `.template` files. Shared files are template expressions such as
`[text].@MissingValue('...')`; inline definitions stay nested in their owner.
Input-data JSON files are ignored during compilation. `--template` applies only
to C# generation. All sources undergo syntax validation even when selecting one
template. Missing names may depend on caller scope and are resolved at execution.

Syntax errors report the relative path, original line/column, and source excerpt,
and prevent publication. Existing output stays unchanged on failure; successful
files are replaced atomically one at a time. Directory publication is not transactional on I/O failure. Compiler exit codes are 0 for success, 1 for
syntax/generation diagnostics, and 2 for usage or filesystem errors. Unfinished
execution features generate stubs rather than generation diagnostics.

## JSON and C# model

`TextTemplateCompiler.CompileDirectory(root)` returns a `CompilationResult`.
Its `Document` is available only when parsing succeeds. Trees are C# records
(`SyntaxNode` with a `SyntaxKind` enum, child lists, and source spans), consumed
directly by the C# emitter. JSON is the export format, not an intermediate
parser dependency.

The JSON document has these fields:

- `schemaVersion`: currently 1.
- `offsetEncoding`: `UTF-16`.
- `sources`: an ordinally sorted map keyed by root-relative paths, for example
  `templates/simple.template` and `subtemplates/xformatPet.template`.

Each source entry contains:

- `name`: path within its category, without `.template`.
- `category`: `templates` or `subtemplates`.
- `text`: the original source text, including comments and whitespace.
- `tree`: the typed syntax tree.
- `tokens`: lexical tokens, including skipped comments/whitespace marked
  `isTrivia: true`, and EOF. Token text reflects legacy normalization.
- `definitions`: encountered inline subtemplate names and their source spans.
- `references`: encountered named subtemplate references, `.#method` calls,
  and literal `@Include('#name')` arguments, with source spans.

Every node has `kind`, `span`, `children`, and an optional `value`. A span contains
`offset`, `length`, `line`, and `column`: offsets/lengths are UTF-16 code units;
lines/columns are one-based and refer to the original source. CR removal and
four-space tab expansion match interpret() for lexing, with offsets mapped back
onto the original text. A tab still occupies one original-source character.
Original source slices are obtained from `text` and the span. Comments and
whitespace skipped by the grammar remain available in the token list and source.

For `Hello {firstName}!`, the tree shape (spans omitted here) is:

```json
{
  "kind": "Template",
  "children": [
    { "kind": "Text", "value": "Hello", "children": [] },
    { "kind": "Whitespace", "value": " ", "children": [] },
    {
      "kind": "Interpolation",
      "children": [
        { "kind": "Identifier", "value": "firstName", "children": [] }
      ]
    },
    { "kind": "Text", "value": "!", "children": [] }
  ]
}
```

Node conventions:

| Kind | Value and ordered children |
| --- | --- |
| Template / SharedTemplate | Main contents / shared template expression |
| Text / Whitespace / NewLine / Continuation / Bullet | Normalized token text |
| Interpolation | Expression |
| Identifier / NamedSubtemplate | Identifier path / name without `#` |
| String / Number / Regex | Raw literal spelling (quotes, signs, regex flags retained) |
| InlineTemplate | Contents, including any nested subtemplate sections |
| SubtemplateSection | Context definitions and intervening text |
| Context / ContextWithFallback | `explicit`: context, body, optional fallback; `implicit`: body, optional fallback |
| Call | Method name (including `@` or `#`), receiver followed by Arguments nodes |
| InlineCall | Receiver, inline template, Arguments nodes |
| Arguments | Arguments in order; separate groups retain the grammar's `arguments*` behavior |
| Group / Not | Child predicate |
| Relational / Logical | Operator text, left and right operands |
| Arrow / ThinArrow | Operator text, predicate, success expression, optional fallback |

AND and OR have equal precedence and associate left, as in the frozen grammar.
Annotations remain ordinary named Call nodes in their original order; later
compilation must implement the interpreter's annotation scope and execution order.
The syntax tree is distinct from the hierarchical composition structure produced
when a template executes.

Definition/reference lists are occurrence indexes, not a global symbol table:
source spans and the tree preserve nested scope, duplicates, and call structure.
Recursive calls remain references, not expanded bodies. Parsing does
not resolve names, execute includes, fetch URLs, or evaluate computed names.
Missing references therefore are not syntax errors; generated named calls resolve
against the active local scope and shared dictionary at execution. All on-disk sources
are represented even if no template references them.

## Validation

Build the existing Node reference tools once for the independent ANTLR oracle:

```sh
npm ci --ignore-scripts --prefix tools/node-reference
npm run build --prefix tools/node-reference
# Set JAVA to an installed Java executable if it is not on PATH.
dotnet test TextTemplate.slnx --nologo
```

The test-only `tools/node-reference/parse-oracle.mjs` loads the original generated
JavaScript lexer/parser. It compares the full reference collection's significant
token types/text and syntax-tree structure against the handwritten C# parser.
The oracle translates ANTLR tree nodes into the common comparison shape; it does
not implement C# parsing or evaluation. Shared files use the interpreter's `{:...}`
wrapper in the oracle. Focused cases cover lexical modes, expressions, predicates,
method/inline calls, comments, escaping, regexes, nested subtemplates, and Unicode.
Additional C# tests cover original source locations, malformed input, deterministic
maps, and failure without creating or overwriting output.

The grammar's syntax is preserved, including its unusual argument grouping and
operator precedence. Interpreter-specific evaluation and subtemplate preprocessing
are not replaced by this syntax tree. There is a 256-level parser nesting limit;
exact invalid-script recovery and advanced editor diagnostics are deferred.

Generation tests also compile all reference sources in an independent .NET project,
compare focused execution cases against Node, and verify deferred stub failures.
The standalone execution test removes the input templates before running.

Generated source is intended to be read, edited, and optionally checked in:

- `GeneratedTemplates.cs`: public rendering API and template/shared registries.
- `Templates/Simple.cs`, etc.: one instance class per top-level template.
- `Includes/DoInclude.cs`, etc.: one file per statically identified `@Include` unit.
- `Includes/Common.cs`: other shared units, each retaining its own class scope.
- `Support/TemplateContent.cs`: structured fragments with `Render()` and `ToString()` for final text.
- `Support/TemplateUnit.cs`: short instance helpers such as `Get`, `Line`, and `InContext`.
- `Support/EvaluationContext.cs`: inherited contexts and compatibility/composition helpers.
- `Program.cs`: console host.
- `GeneratedTemplates.csproj`: standalone .NET 10 executable with explicit source items.

Subtemplate calls read as `ApplySubtemplate("anded", SubtemplateInput("formatPet"))`.
`SubtemplateInput` collects the named subtemplate's results in the current item
contexts; `ApplySubtemplate` preserves dynamic lookup and argument binding.
Definition registration lives in separate methods using `definitions.Add(...)`.
`WithDefinitions` creates the same inherited scope when the body is reached;
registration stores callbacks without evaluating the subtemplates. Included
units use separate registration methods with their original evaluation rules.
Conditional results use a data-derived name such as `petsResult` when available.

Inline definitions remain private methods in their owner's file, retaining names
such as `formatPet`. Names contain neither category prefixes nor source coordinates.
Comments show the original template fragment above each generated method or
deferred operation, followed by its source location; diagnostics retain locations.
Identifier collisions receive `_2`, `_3`,
etc.; keyword identifiers are escaped. File/class names are allocated deterministically
and avoid case-insensitive collisions. Anonymous body methods describe their content.

Generated locals and method returns use their proven C# types: `bool` for predicates,
`double` for numbers, `string` for text, and `TemplateContent` for structured fragments.
Conditional branches retain their common type and become nullable when needed.
Dynamic lookups, list evaluation, and unfinished operations retain `object?`;
the generator does not assume a type from sample data.

Simple templates use readable local values and structured interpolated strings:

```csharp
private TemplateContent formatPet()
{
    object? type = Get("type");
    object? name = Get("name");

    return Line($"a {type} named {name}");
}
```

`Line` returns `TemplateContent`. Its `Render()` and `ToString()` produce final text;
internal composition traverses fragments directly so nested content is not flattened early.
Argument arrays remain separate from template content. A nonnullable `TemplateContent` means
that the fragment container exists, not that every fragment has a value.
`Line($"...")` uses a custom interpolated-string handler that retains nulls and
missing-value markers. Composition removes the entire affected line, including
surrounding literals, and keeps unaffected lines. Consequently generated bodies
need neither eager null guards nor a nullable return solely because a lookup
may be missing. Only final `Render()` / `ToString()` converts wholly suppressed
content to `""`; doing that during nested interpolation would lose suppression.
The birthday-row regression cases cover optional null names, required null or
absent names, and null results from a nested date subtemplate against Node.

`Line` retains object-valued holes, missing values, and nested fragments until final
composition; it does not append a newline. Raw multiline strings preserve visible
layout. Literal expressions are folded into content where equivalent. Nested unfinished
operations are deferred local functions, with separate receiver/argument expressions.
No branch or stub operand is eagerly evaluated for the sake of a readable local.
Child contexts inherit local bindings and data without changing the caller's context.
Generated callbacks name their context by role: `itemContext` for per-item evaluation,
`dataContext` for data contexts, `definitionContext` for definition scopes,
`annotatedContext` for annotations, and `includedContext` for included units.
See [DESIGN.md](../../DESIGN.md#11-editable-generated-c-layout-and-readability-contract)
for the full readability contract.

Regeneration overwrites matching files, including edits. Obsolete units and extra
files remain on disk but are not compiled unless added to the project. For a clean
regeneration, delete `Generated/` first; this also removes the old
`templates/Template_*.cs` layout. This repository treats generated output as
disposable and does not require automatic backups.
Literal shared `@Include` execution is supported for the characterized subset:
exported definitions, literal missing-value annotations, and annotation ordering.
Computed/local includes and other include annotations remain explicit runtime stubs.
`GenerationResult.Files` exposes the deterministic source map; `Source` retains the
combined-file form for existing callers.

Named data contexts such as `{#data:[/data/people]}` resolve `/data/people` to
`people.json` under `--data-root`. Names may be URL encoded; directories and HTTP
resources are not supported. Missing or invalid files fail without publishing output.
`--data-file` still supplies the initial context independently. Embedded callers can
pass the directory as `GeneratedTemplates.Render(name, data, dataRoot)`.

```sh
dotnet run --project Generated -- --template outline --data-root reference/data
```

Data loading is supported independently of outline formatting: outline may still
reach runtime stubs for other unfinished features.

`@MultilineStyle` validates literal style arguments and inherits them into child
contexts; an empty argument list clears the setting. Multiline field values retain
those styles until composition knows their insertion point. Rendering supports
`Indented`, `IndentAllButFirst`, `Padded`, `Tabbed`, `Trimmed`, and `Offset`, including
combinations. It preserves the legacy four-space indentation, first-output-line
exception, ECMAScript trimming, trailing blank-line removal, and padding newline.
Literal template text and internal `$n` strings are not styled again. Dynamic style
arguments remain a TODO.
Exact Node/C# comparisons are recorded in `multiline-cases.json`.

Bullets retain their newline, indentation, and following spaces as structured
markers. Final rendering assigns levels by indentation and tracks counters through
nested lists and returns to earlier levels. `@BulletStyle` supports literal styles,
numeric/alphabetic/Roman counters, initializers such as `1:10` and `i:iv`, compound
styles such as `1.1`, padding, and per-level style fallback. `@BulletMode` supports
implicit and explicit reset behavior; `@DefaultIndent` controls automatic list
indentation. Changing bullet styles resolves that scope before restoring the caller's
styles, matching Node. A missing line can still establish a bullet level.
Characterization cases are in `bullet-cases.json`; the unfiltered outline fixture
isolates layout from the unfinished `Where` filter. Dynamic annotation arguments
remain TODOs. No full-outline filtering parity is claimed.

`IfMissing` returns its first argument for Missing, empty string, zero, or false;
other values pass through. Explicit null receivers remain null, matching the
legacy call dispatch. No arguments yields missing output on fallback; extra
arguments are ignored. Characterized against Node in `ifmissing-cases.json`.

`Count()` returns a numeric list size (rendered as decimal text), zero for Missing,
and one for scalar or dictionary receivers. Explicit null passes through. Argument
lists count truthy, non-missing items. `Count(predicate)` remains unimplemented.
Node comparisons are recorded in `count-cases.json`.

Named template methods accept object/list receivers as `$0` and object/list
arguments as `$1`, `$2`, etc. These retain their existing data parent links; the
method call context is a child of the caller. Node comparisons cover field lookup,
list counts and iteration, arguments, and parent access (`named-data-cases.json`).

TODO: `Where` is currently an authorized pass-through: it returns its receiver
unchanged and does not evaluate its predicate. Implement per-item filtering before
claiming legacy parity. TODOs in source also track dynamic style arguments,
and the unimplemented conditional `Count` form.

`Case(match, result, ..., default)` compiles to a C# switch with lazy guarded
cases and result branches. Matching preserves legacy ordinal string equality and
integer-parsed numeric equality. TODO: characterize even-length and too-short
argument forms; these remain runtime stubs. Node cases: `switch-cases.json`.

`@Include` accepts computed names, including `[{'#style'}{$1}]`. Compiled import
bindings resolve local definitions before shared units and retain their annotations
and exported definitions within the caller's annotated scope. Include bodies run
without publishing their text. Dynamic selection retains all available shared units
in selected-template builds; no runtime template parsing is used. Included units
with unsupported non-annotation call chains still have explicit runtime stubs.
`dynamic-include-cases.json` checks local/shared dispatch, exports, scope, and all five
style choices from an unfiltered copy of the bullets template.
