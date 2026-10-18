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

Equality initially means **exact output characters**, including indentation, spaces, tabs, line endings, punctuation, and numbering. Errors and edge-case behaviors should also be characterized; do not invent semantics where evidence is missing.

The **authoritative A-side evaluator is the original TypeScript interpreter running under Node.js**. The A/B harness must record its source revision, inputs, and execution context so that comparisons remain reproducible. Preserve JavaScript behavior explicitly where C# defaults differ, including truthiness, missing/null values, coercion, ordering, and formatting.

## 4. Source-generation boundary

### Build time

1. Parse existing TextTemplate scripts using the original ANTLR grammar or its faithful C# generated counterpart.
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

- Generate C# lexer/parser/visitor from the unchanged ANTLR grammar.
- Parse the original sample corpus and capture syntax diagnostics.
- Establish parser regression tests before code generation.

### Milestone 3 — Minimal standalone generation

- Generate a simple C# application from a literal-only template.
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
2. What is the precise generated-program entry point and input representation (JSON, typed objects, or both)?
3. Which TypeScript composition routines should become emitted C# helpers, and which should become template-specific generated logic?
4. What are the exact scoping rules and evaluation order for `@` annotations?
5. How are hierarchy, tabs, bullets, and numbered lists represented before rendering?
6. How should the harness compare errors and other nondeterministic effects?
7. Which ANTLR4 tool and C# runtime versions should be used with .NET 10 to preserve the unchanged grammar’s parsing behavior?

## 10. Working principles

- Treat the original scripts as a stable compatibility surface.
- Preserve the reference repository unchanged.
- Prefer evidence from execution and tests over inferred semantics.
- Generate inspectable C# to make debugging straightforward in CodeServer.
- Keep every A/B test independently reproducible.
- Distinguish build-time tooling from generated runtime code.
- Avoid pushing or deleting remote branches without explicit user approval.
ar