# TextTemplate — Design and Implementation Plan

## 1. Purpose

TextTemplate is an existing template language whose scripts must continue to work unchanged. This project replaces runtime interpretation with a **source generator** that produces ordinary C# code. The generated code must accept equivalent input data and produce the **same output** as the established TextTemplate evaluator across a broad corpus of scripts and data.

The initial goal is **compatibility, not language redesign**. Future changes to grammar and semantics may be considered after a dependable A/B test suite exists.

## 2. Existing source material

The preserved original repository is `../texttemplate-editor` (`https://github.com/eisnerw/texttemplate-editor`). Useful paths include:

| Reference | Path |
| --- | --- |
| Lexer grammar | `src/main/antlr/TextTemplateLexer.g4` |
| Parser grammar | `src/main/antlr/TextTemplateParser.g4` |
| TypeScript interpreter | `src/main/typescript/TextTemplateInterpreter.ts` |
| Parser integration | `src/main/typescript/ParserFacade.ts` |
| Data fixtures | `nodeserver/data.json` |
| Template fixtures | `nodeserver/templates.json` |
| Subtemplate fixtures | `nodeserver/subtemplates.json` |
| Existing parser tests | `src/test/javascript/` |

Develop the new C# implementation directly from the **existing TypeScript interpreter and helper code**, including `Externals.ts`. Translate the algorithms needed for generated template logic and emitted helpers, validating their behavior against the Node.js reference runner. Legacy C# source recovery, a `legacy-csharp/` directory, and a `TextTemplate.Legacy` project are not part of the plan.

Treat the original TypeScript sources and grammar as a fixed reference snapshot. Record the source commit, but do not create synchronization machinery.

## 3. Functional contract

Given a template script `T`, subtemplate definitions `S`, and input data `D`, the new compiler generates C# sources `G(T, S)`; compiling and executing those sources with `D` should produce the same observable result as the unchanged TypeScript evaluator:

```text
TypeScriptEvaluate(T, S, D) == Execute(CompileCSharp(Generate(T, S)), D)
```

Equality initially means **exact output characters**, including indentation, spaces, tabs, line endings, punctuation, and numbering. Valid-script behavior is the initial compatibility requirement. For malformed input, report a reasonable diagnostic with its original source location and stop publication; ANTLR error wording and recovery need not be reproduced. Early releases use texttemplate-editor as the template design studio.

The **authoritative A-side evaluator is the original TypeScript interpreter running under Node.js**. The A/B harness must record its source revision, inputs, and execution context so that comparisons remain reproducible. Preserve JavaScript behavior explicitly where C# defaults differ, including truthiness, missing/null values, coercion, ordering, and formatting.

## 4. Source-generation boundary

### Build time

1. Parse existing TextTemplate scripts using a handwritten C# lexer and parser, with the unchanged ANTLR grammar as the syntax specification. ANTLR remains a test oracle, not a C# runtime dependency.
2. Translate the TypeScript implementation’s observed semantics into template-specific C# expressions/statements and supporting types.
3. Emit readable, deterministic C# source files and any necessary helper source.

### Runtime

The compiled generated program:

- receives the input data and necessary context;
- evaluates compiled template-specific logic;
- builds structured composition results as required;
- renders the final text;
- **does not depend on any TextTemplate project DLL** and does not re-parse TextTemplate scripts.

Normal .NET library dependencies are permitted; any external runtime dependency should be deliberate, documented, and minimal. The default design is to **emit helper source code**, not require a TextTemplate runtime NuGet package.

## 5. Semantics that must be preserved

These areas are known to be important, but their precise algorithms must be verified against the reference:

- Context selection and contextual evaluation (including context changes within a template).
- Embedded expressions, method invocation, and method chaining.
- Predicates, logical operations, relational operations, conditional expansion and arrows.
- Named and inline subtemplates, including subtemplate invocation and scoping.
- Special `@` annotation functions; **scope and execution precedence relative to ordinary methods matter**.
- Hierarchical **tab-based indentation**, line breaks, bulleting, numbering, and formatting.
- Non-streaming composition: intermediate structured fragments/tree may be necessary before final rendering.
- Escaping, empty/missing/null values, conversion, ordering, and error reporting.

Do not optimize away observable behavior. Where the legacy interpreter appears inconsistent, capture the actual result with a regression test before proposing a correction.

## 6. Suggested project layout

```text
TextTemplate/
├── AGENTS.md
├── DESIGN.md
├── src/
│   ├── TextTemplate.Compiler/   # parser integration, semantic analysis, C# emitter
│   └── TextTemplate.Core/       # development-time abstractions/composition prototypes
├── tests/
│   └── TextTemplate.Tests/      # fixtures and A/B harness
└── reference/
    ├── grammar/                # versioned copies of original grammars
    ├── fixtures/               # input/templates/subtemplates
    └── typescript/             # unchanged reference interpreter and host sources
```

This is an initial organization, not a mandate for unnecessary abstractions. **TextTemplate.Core may inform helper implementations, but generated applications must compile without referencing it.**

## 7. A/B testing design

The named values in `reference/fixtures/data.json` are also available individually
as `reference/data/NAME.json`. The original collection remains unchanged. Both
the generated CLI and the Node runner accept `--data-file FILE` containing the
input value itself. The generated CLI loads that file at runtime, independently
of the chosen template name; it does not infer associated data or embed the data
during generation. This input option does not resolve template resource URLs or
change the execution features currently supported by generated C#.

Each fixture should specify:

- Script or script identifier.
- Input data (including the shape and type assumptions).
- Named subtemplate definitions, if applicable.
- Execution options/context, if any.
- Reference evaluator and observed output or error.

Test runner procedure:

1. Execute the unchanged TypeScript interpreter through the Node.js reference runner and capture its exact output.
2. Generate C# in a clean temporary output folder.
3. Compile the generated code as a standalone .NET project **without TextTemplate assembly references**.
4. Execute generated code using equivalent data.
5. Compare results and report first mismatch, escaped whitespace, and relevant input identifiers.
6. Preserve passing cases in CI and rerun the corpus after each compiler change.

Use broad fixtures from `data.json`, `templates.json`, and `subtemplates.json`, augmented by focused tests for indentation, annotation ordering, predicates, context scoping, formatting, and error paths. If repeatability requires stable culture, timezone, or input ordering, control those conditions in **both** executions and record them.

## 8. Incremental milestones

### Milestone 0 — Inventory and baseline

- Inventory the fixed original repository and record its Git commit.
- Inspect grammar, interpreter API, original fixtures, callback handling, and existing Node server routes.
- Preserve unchanged reference copies as appropriate.

### Milestone 1 — Standalone Node.js reference runner (**first Codex assignment**)

- Invoke the existing `TextTemplateInterpreter.interpret(input, callback, options)` directly from Node.js.
- Replace browser worker / `ParserFacade` AJAX hosting with a small filesystem-backed callback adapter. Handle `result`, `url`, and `status` messages; preserve callback timing and failure semantics.
- Examine only the callback-relevant part of `ParserFacade.ts`; Monaco editor support is out of scope.
- Use the lookup semantics of the existing `nodeserver` routes and original `templates.json`, `subtemplates.json`, and `data.json` fixtures. Check associated-template data, array joining, absent keys, and any shared-subtemplate preprocessing for observable effects.
- Provide CLI input for a template identifier or template text, data, and fixture location. Write exact rendered output to stdout, diagnostics to stderr, and fail with nonzero exit status on errors.
- Run real fixtures; record deterministic golden outputs along with inputs and source revision. Do not modify the interpreter, original grammar, or reference repository. Do not implement C# generation in this milestone.

### Milestone 2 — C# parsing

- Implement a handwritten C# lexer and recursive-descent parser against the unchanged grammar.
- Build typed C# syntax records with original source spans and optional JSON export.
- Parse the entire root directory’s templates/ and subtemplates/ collections into a deterministic map keyed by relative source path, retaining inline definitions and references.
- Complete syntax validation before emitting the JSON document; any syntax failure produces diagnostics and no new output. Named calls retain runtime scope lookup; missing names are not syntax failures.
- Compare tokens and normalized tree structure against the original JavaScript ANTLR parser, including the real fixture corpus.
- See [the compiler guide](src/TextTemplate.Compiler/README.md) for commands, JSON schema, and tests.

### Milestone 3 — Minimal standalone generation

- Generate standalone C# by default; retain the comprehensive parse-tree map through `--format json`.
- Emit explicit runtime `NotImplementedException` stubs for unfinished features rather than rejecting valid templates during generation. Report the feature and template source location when reached; unexecuted branches remain harmless.
- Compile the entire reference collection and compare supported execution cases against Node.
- Compile, execute, and compare exact text output against the Node reference.
- Prove the output project has **no TextTemplate assembly dependency**.

### Milestone 4 — Evaluation semantics

- Add tokens, contexts, methods, predicates, and subtemplates incrementally.
- Translate proven TypeScript helper algorithms into emitted C# source when appropriate, preserving their JavaScript semantics and provenance.
- Expand A/B cases with each feature.

### Milestone 5 — Hierarchical composition

- Translate the TypeScript interpreter’s structured, non-streaming composition and rendering behavior into C#.
- Validate tabs, newlines, bullets, nesting, numbering, annotations, and precedence against edge cases.

### Milestone 6 — Broad compatibility

- Run the full template/data corpus; classify remaining mismatches.
- Resolve disputed behavior by inspecting the TypeScript implementation and adding focused cases to the Node.js reference suite.
- Document intentionally unsupported scenarios rather than hiding failures.

### Milestone 7 — Editor integration

- Revisit Monaco only after the compiler and compatibility suite are reliable.
- Integrate with a .NET backend as needed, and consider language improvements later.

## 9. Open design questions

Do not resolve these by assumption:

1. Which JavaScript behaviors require explicit compatibility helpers in C# to preserve the TypeScript interpreter’s results?
2. The initial generated API is `GeneratedTemplates.Render(name, JsonElement? data)` with a `--template` / `--data-file` CLI. Should a later API also support typed objects?
3. Which TypeScript composition routines should become emitted C# helpers, and which should become template-specific generated logic?
4. What are the exact scoping rules and evaluation order for `@` annotations?
5. How are hierarchy, tabs, bullets, and numbered lists represented before rendering?
6. How should the harness compare errors and other nondeterministic effects?
7. How should computed subtemplate references be bound before executable generation? The .NET 10 parser itself has no ANTLR runtime dependency.

## 10. Working principles

- Treat the original scripts as a stable compatibility surface.
- Preserve the reference repository unchanged.
- Prefer evidence from execution and tests over inferred semantics.
- Generate inspectable C# to make debugging straightforward in CodeServer.
- Keep every A/B test independently reproducible.
- Distinguish build-time tooling from generated runtime code.
- Avoid pushing or deleting remote branches without explicit user approval.

## 11. Editable generated C# layout and readability contract

The output is ordinary C# intended for human maintenance. Readability is an explicit
acceptance criterion alongside character-for-character compatibility. Generated code
must reflect template operations rather than the individual nodes of the parser tree.

```text
Generated/
    GeneratedTemplates.cs           # Public dispatch and construction
    Program.cs                      # Console host
    GeneratedTemplates.csproj        # Standalone project
    Templates/
        Wikipedia.cs                # One top-level template per file
        Simple.cs
    Includes/
        DoInclude.cs                # One literal @Include source unit per file
        Common.cs                   # Other shared units grouped together
    Support/
        TemplateUnit.cs             # Short instance helpers: Get, Line, InContext
        EvaluationContext.cs        # Compatibility and structured composition
```

Inline definitions stay in their owner's class as private methods; local functions
are appropriate for deferred operations inside a method. A single partial class was
an early possibility, not a requirement. Separate instance classes provide independent
member scopes while sharing small helpers through `TemplateUnit`. Grouped shared units
retain independent classes so their private definitions cannot leak into each other.
Literal `@Include` references determine dedicated include files; computed includes
cannot yet be identified statically. Their execution remains explicitly unfinished.

Preserve template-defined names such as `formatPet`, `getWiki`, and `wikiapi`. Do not
add `Template_`, `Subtemplate_`, `Define_`, syntax-kind names or source coordinates.
Use meaningful names for anonymous context bodies, and put source locations in
comments/diagnostics instead. Method and deferred-operation comments show the
original template fragment first, preserving its layout, followed by the source
location. These comments are documentation only, never runtime interpretation
input. Sanitize invalid identifier characters, escape C#
keywords, and allocate `_2`, `_3`, etc. only for actual collisions. Track class member
names separately from each method's locals/functions; bind each definition to its
allocated name. Class/file names also avoid case-insensitive collisions.

Small expressions and literals stay inline. They do not get methods merely because
they are parser nodes. Template content should resemble this generated method:

```csharp
private object? formatPet()
{
    var type = Get("type");
    var name = Get("name");

    return Line($"a {type} named {name}");
}
```

`Line` accepts a custom interpolated-string handler which retains object-valued holes
and nested fragments. It adds no newline and performs no early string conversion.
Multiline content uses raw interpolated strings so its whitespace remains visible.
Local assignments preserve left-to-right evaluation of content holes. Branches,
logical short-circuiting and unfinished-call operands remain deferred; extracting a
local must never move evaluation out of its original execution scope. Nested
unfinished operations use short local functions with locations in their diagnostics,
instead of deeply nested diagnostic wrappers. Their receiver/arguments remain visible
for future implementation, but are not executed by a stub.

Child contexts inherit data and local definitions without mutating the caller. They
share a recursion-depth counter; named definitions execute against the invocation
context. Runtime name resolution is retained where caller scope can affect binding.
Literal shared includes, literal missing-value annotations, scalar named-method
arguments, and scalar Join are now supported as described below. Full bullet hierarchy
and other unfinished features still have runtime stubs.

C# generation defaults to `Generated/`. Generated sources and their project may be
checked in. This repository treats `Generated/` as disposable compiler output;
automatic backups are not required. Regeneration overwrites matching files, including manual edits; obsolete
files remain on disk but are excluded from the project's explicit compile list. Syntax
validation completes before publication. Individual writes are atomic, but a directory
update is not transactional on I/O failure. The deterministic `GenerationResult.Files`
map and combined `.cs` output are both supported. Tests compile both independently
of TextTemplate assemblies, compare execution with Node, and check the readability
contract (including keywords, name collisions, scope isolation and editable output).

## 12. Includes and the simpledata singleton-data path

Source inspection uses `invokeMethods`, `visitNamedSubtemplate`, `callMethod`, and
`processSubtemplates` in the preserved TypeScript interpreter. Characterization
cases in `tests/node-reference/include-cases.json` freeze the observed results;
the independent Node tests and C# A/B tests run the same inputs.

- Literal `@Include('#name')` imports a shared unit before evaluating its receiver.
  Exported definitions and annotation values are inherited by that receiver's child
  context and do not replace the surrounding caller's bindings.
- Annotations in a method chain execute in source order, before ordinary methods
  or the receiver. A later missing-value annotation overrides an earlier include's
  replacement, while a later include can override the earlier replacement.
- Include loading preprocesses a leading `Subtemplates:` section. The emitter
  projects the already parsed leading marker/definition nodes into a section,
  matching the legacy include preprocessing without changing lexer/parser syntax.
- `@MissingValue('text')` attaches the replacement to a missing value at lookup;
  `{key}` substitutions use the original lookup path. Predicate truth remains false.
- Named methods evaluate with `$0` as the receiver and `$1`, `$2`, etc. as scalar
  arguments in a child data context. `^` refers to the caller's data.
- Legacy `Join` leaves scalar input unchanged. List joining is described below.

The exact `simpledata` / `reference/data/nested.json` invocation and escaped-name
variant are compared against Node. Singleton arrays use the established legacy
collapse behavior; multi-element lists are now supported as described below. Computed/local
include targets, nonliteral annotation arguments, unsupported include layouts,
and additional include annotations still fail explicitly when executed. These
limits must not be mistaken for general annotation or list parity.


## 13. List contexts and joining

The unchanged interpreter's `TemplateData`, `visitMethodableTemplateSpec`,
`invokeMethods`, `Join`, and `doCompose` are the behavioral sources. The list
characterizations in `tests/node-reference/list-cases.json` record exact outputs
against reference commit `ccedefd87b012ff1173ee2f43323b6eb7c8bd268`; Node and
standalone C# tests execute the same inputs independently.

- Data lists retain item order and parent links: an item's parent is its containing
  list, whose parent is the enclosing dictionary. Singleton arrays collapse.
  Empty array properties are absent, so context fallbacks and missing annotations
  apply to the property name.
- Inline and named templates evaluate in each item context, inheriting local
  definitions and annotations. Missing rendered items are omitted; a single
  surviving result collapses and no surviving result becomes missing `list`.
  A root template itself is not repeated for root-array input.
- Rendered lists remain structured until final composition, including nested lists
  and legacy indentation. They are distinct from data lists and method argument
  lists. Debug summaries preview all three without traversing parents.
- Method receivers evaluate once per item before the method chain runs. The chain
  receives an argument list; named methods store it in `$0`, allowing the fixture's
  `#anded()` to call `$0.Join(...)`. Parent-prefixed receivers bypass this mapping.
- `Join` filters missing/false/empty items, defaults to comma-space, and accepts
  an optional final separator. String data lists also support joining; arbitrary
  object data formatting remains unfinished.
- When every item is missing, a multi-item method argument list joins to an empty
  string, preserving surrounding text. A singleton data array collapses before
  evaluation, so its missing scalar receiver propagates through the same method
  chain and suppresses the surrounding line. The `all-missing-named-join-1` and
  `all-missing-named-join-3` cases freeze this legacy distinction.

The end-to-end regression now includes `simpledata` with `nested`, `escapeTest`,
and the three-pet `simpledata` dataset. This is incremental list support, not full
language parity: filtering, grouping, bullets, general data formatting, and
additional methods/annotations retain their existing deferred failure behavior.
