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
- Cover invalid scripts, missing values, exception behavior, and other edge cases as observed rather than guessed.
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
3. Establish C# parsing from the unchanged grammar.
4. Implement the smallest generator feature needed for one matching test.
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
