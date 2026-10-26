# TextTemplate — Codex Instructions

## Objective

Build a C# source-code generator that translates **existing TextTemplate scripts** into standalone C# code. Given identical template scripts, subtemplates, and input data, the generated program must reproduce the legacy interpreter's output **character for character**.

**Behavioral equivalence is the overriding acceptance criterion.** Do not revise the language syntax or intentionally fix legacy behavior until equivalence is established and a change is explicitly approved.

## Reference implementations and sources

- Original repository, read-only reference: `../texttemplate-editor` (`eisnerw/texttemplate-editor`).
- Relevant grammar: `src/main/antlr/TextTemplateLexer.g4` and `TextTemplateParser.g4`.
- Existing TypeScript interpreter: `src/main/typescript/TextTemplateInterpreter.ts`.
- Hosting/callback references: `src/main/typescript/ParserFacade.ts`, `src/main/typescript/TextTemplateWorker.ts`, and `nodeserver/routes/` (including `runonserver.js`).
- Initial fixtures: `nodeserver/data.json`, `nodeserver/templates.json`, and `nodeserver/subtemplates.json`.
- Develop the C# implementation directly from the existing TypeScript interpreter and its helper code, including `Externals.ts`. Legacy C# source recovery and a `legacy-csharp/` reference directory are not needed.

Do not modify the reference repository without explicit instruction. The unchanged TypeScript interpreter running under Node.js is the authoritative behavioral reference for C# development. Characterize TypeScript/JavaScript behavior with tests before translating it to C#.

## Nonnegotiable architecture

1. The generator consumes the **current** TextTemplate syntax without changes.
2. The generator emits human-readable, compilable C# sources.
3. Generated C# runs **independently of the TextTemplate project**: it must not require TextTemplate.Compiler, TextTemplate.Core, or any other TextTemplate assemblies at runtime.
4. Helper algorithms may be translated or adapted from the TypeScript interpreter and its helper code and **emitted as source** alongside template-specific generated code. Keep provenance and licensing clear.
5. Generate executable template-specific logic; do not ship the original template text for runtime interpretation.
6. Preserve contexts, subtemplates, expression/predicate semantics, method invocation, special `@` annotations (including ordering and scope), tab-dependent hierarchy, bullets, numbering, and whitespace.
7. The target composition is **structured and non-streaming**: retain hierarchical content before rendering the final text, where required for correct legacy behavior.
8. The parser/grammar is frozen for the initial compatibility phase. Propose syntax changes separately and only after tests establish parity.

## Validation strategy

For each A/B case use identical script, subtemplates, data, and relevant execution context:

- **A:** Execute the unchanged TypeScript interpreter using the Node.js reference runner.
- **B:** Generate C#, compile it as a separate project, run it without TextTemplate assembly references, and capture its output.
- Compare bytes or characters as appropriate, explicitly including tabs, spaces, CR/LF, indentation, ordering, bullets, and numbering.
- Record first mismatch with escaped surrounding characters and the test-case inputs.
- Prioritize valid-script parity; texttemplate-editor is the initial design studio. Syntax errors must identify the original source location and prevent output publication. Exact ANTLR error wording/recovery is not required. Continue characterizing missing values, runtime exceptions, and other observed evaluation behavior.
- Keep every verified behavior as a regression test. Do not share semantic evaluation code between the reference evaluator and the generated implementation in the A/B harness.

## Immediate priority: Node.js reference evaluator (Milestone 1)

**Do this before implementing the C# parser or generator.** The frozen TypeScript interpreter is the authoritative executable A-side oracle.

1. Inspect `TextTemplateInterpreter.ts` and its `interpret(input, callback, options)` API.
2. Inspect **only the relevant callback/data-resolution portions** of `ParserFacade.ts`, plus `TextTemplateWorker.ts` and the `nodeserver` routes. Most of ParserFacade is Monaco-specific and must not be ported.
3. Create a small Node.js CLI host using the existing interpreter. Handle `result`, `url`, and `status` callback events; resolve template, subtemplate, and data lookups directly against the original JSON files, without a browser, worker, Express server, or HTTP calls.
4. Preserve asynchronous callback semantics, array-to-newline conversion, associated template data, missing-entry behavior, and any shared-subtemplate preprocessing **if shown necessary by source inspection or tests**. Do not assume the editor's `/*SHARED*/` behavior can be ignored.
5. Keep the interpreter/grammar/reference files unchanged. Write exact rendered text to stdout; diagnostics to stderr; return nonzero on failures. Test real fixture cases, including nested contexts and subtemplates.
6. Document the CLI contract and tests. Present a brief plan before substantial implementation. **Do not start C# code generation during this milestone.**

## Subsequent workflow

1. Document observed semantics and uncertainties in `DESIGN.md` or targeted notes.
2. Freeze verified reference outputs as golden fixtures, recording the interpreter version/source commit and evaluation inputs.
3. Implement handwritten C# parsing from the unchanged grammar, without an ANTLR C# runtime dependency. Keep typed syntax records and source spans; export a root-wide JSON map only after all files pass syntax validation.
4. Generate standalone C# by default, with optional `--format json` tree export. Emit runtime `NotImplementedException` stubs for unfinished execution features, identifying feature and original source location when reached; do not reject valid syntax because a feature is unfinished. Implement each feature against a matching test.
5. Compile and execute generated standalone C#; compare exact results against the Node reference.
6. Translate the next required behavior from the TypeScript implementation, add focused regression tests, and repeat against the Node.js reference. Preserve JavaScript semantics explicitly where C# defaults differ.

Make incremental, reviewable changes. Explain significant design decisions before implementation. Ask when semantics are ambiguous. Do not claim success without running the relevant tests. Never silently alter grammar, reference sources, or fixtures to make tests pass.

## Repository and environment

- New repository: `eisnerw/TextTemplate`.
- Adjacent reference checkout: `../texttemplate-editor`.
- Development environment: Ubuntu 24.04 ARM64, .NET 10, CodeServer with Codex.
- Planned projects: `TextTemplate.Compiler`, `TextTemplate.Core` (build-time/support development), and `TextTemplate.Tests`. **No generated deployment may depend on their compiled assemblies.**
- Follow `DESIGN.md` for milestones and open design questions.

## Git and security

- Do not push, force-push, delete branches, or rewrite Git history without explicit approval.
- Do not copy credentials, tokens, private keys, old executable artifacts, or unreviewed configuration from the compromised former server.
- Avoid committing generated build outputs or secrets.

## Generated C# readability contract

Readability is an acceptance criterion alongside behavioral parity. Organize output
around maintained template operations, not syntax nodes. Follow DESIGN.md section 11:

- One file per top-level template and literal shared `@Include` unit; group other
  small shared units. Keep inline definitions as private methods/local functions
  in their owner's file. Put compatibility helpers under `Generated/Support/`.
- Preserve meaningful definition names (`formatPet`); no `Template_`, `Subtemplate_`,
  `Define_`, syntax-kind or source-coordinate prefixes/suffixes in generated names.
  Use deterministic `_2`, `_3` suffixes only for actual collisions, with separate
  member and local scopes. Handle C# keywords and invalid characters.
- Use instance methods, inherited child contexts, readable locals (`type`, `name`),
  and structured interpolated strings (`Line($"a {type} named {name}")`).
  Literals and simple expressions must not get their own generated methods.
- Keep source locations in comments or diagnostics. Split complex operations into
  readable statements/functions without eagerly evaluating deferred branches or
  annotation/stub operands. Do not flatten structured values into strings early.
- Generated method comments show the original template fragment, preserving its
  layout, followed by its source location. Locations alone are not sufficient.
- Generated source may be checked in and edited. Review representative output and
  maintain naming/layout regressions in addition to independent Node A/B tests.
- Treat this repository's `Generated/` as disposable compiler output. Regenerate
  without automatic backups; preserve manual changes only when explicitly requested.

### Typed fluent output direction

- Target one-time conversion followed by manual C# maintenance. Prefer typed fluent
  template operations over nested compatibility calls; use short readable chains,
  meaningful intermediate values, and explicit control flow for complex branches.
- Separate registration and context-construction plumbing from editable template
  logic. Helpers must preserve caller-context binding, inherited annotations, deferred
  evaluation, and the distinction between collected method inputs and rendered lists.
- Replace `object?` in the authoring surface with meaningful value, data, content,
  and context types where possible. Dynamic representation may remain inside support.
  Do not substitute ordinary LINQ or string semantics for verified legacy behavior.
- Provide JSON-first, side-effect-free data `ToString()` and debugger summaries,
  excluding implementation links such as parents. Use explanatory fallback text when
  faithful JSON is unavailable. Preserve content rendering semantics; debugger inspection
  may preview already-evaluated content in an isolated rendering context, but must
  not execute deferred code, resolve resources, or mutate live rendering state.
- Evolve the existing emitter incrementally under DESIGN.md section 11.1; do not
  duplicate the parser or compatibility implementation for the new output style.

- Original template fragments, paths, and source coordinates belong only in comments.
  Generated runtime diagnostics should identify the maintained C# source. Do not emit
  template source expressions as metadata when semantic flags or data keys suffice.
