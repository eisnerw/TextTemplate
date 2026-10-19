# TextTemplate compiler: parsing phase

The compiler reads an entire root directory, parses its templates and shared
subtemplates, and emits one JSON document containing a map of parse trees. It
uses a handwritten C# lexer and recursive-descent parser based on the unchanged
TextTemplate grammar. The compiler has no ANTLR or Node.js runtime dependency.
It does not evaluate templates or generate executable template C# yet.

## Run

From the repository root:

```sh
dotnet build src/TextTemplate.Compiler
# Parse the complete checked-in collection and write one JSON file
dotnet run --no-build --project src/TextTemplate.Compiler -- --root reference --output /tmp/texttemplate-trees.json
# Or write JSON to stdout (build first so build output cannot contaminate it)
dotnet run --no-build --project src/TextTemplate.Compiler -- --root reference > /tmp/texttemplate-trees.json
# Usage
dotnet run --no-build --project src/TextTemplate.Compiler -- --help
```

The root must contain both `templates/` and `subtemplates/`. Each is scanned
recursively for `.template` files; other extensions and input-data JSON files
are ignored. Files are decoded as UTF-8. Named shared subtemplates are parsed
as template expressions, such as `[text].@MissingValue('...')`, not as top-level
literal text. Inline definitions remain nested inside their owner's tree.

The compiler parses every file, whether referenced or not. It reports the first
syntax error in each invalid file with its relative path, original line/column,
and source excerpt. Any syntax error prevents publication of the JSON document.
An existing output file remains unchanged on failure; a successful result replaces
it atomically. Exit codes: 0 success, 1 syntax errors, 2 usage or filesystem/output
errors. Early releases prioritize valid templates authored in texttemplate-editor;
ANTLR error wording and recovery are not reproduced.

## JSON and C# model

`TextTemplateCompiler.CompileDirectory(root)` returns a `CompilationResult`.
Its `Document` is available only when parsing succeeds. Trees are C# records
(`SyntaxNode` with a `SyntaxKind` enum, child lists, and source spans), consumed
directly by future generation code. JSON is the export format, not an intermediate
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
Recursive calls remain references, not expanded bodies. This parsing phase does
not resolve names, execute includes, fetch URLs, or evaluate computed names.
Missing references therefore are not syntax errors; binding and complete dependency
validation must precede future executable code generation. All on-disk sources
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
