# TextTemplate

TextTemplate translates existing TextTemplate scripts into readable, standalone
C# programs. Given the same scripts, subtemplates, and data, generated C# must
reproduce the legacy TypeScript/JavaScript interpreter's output character for
character, including whitespace, indentation, bullets, and numbering.

The generated executable does not require TextTemplate assemblies, Node.js, or
ANTLR at runtime. Node runs the unchanged legacy interpreter for comparison tests.
The parser uses the existing language syntax; execution support is still being
developed. Valid but unfinished features produce runtime `NotImplementedException`
diagnostics when reached.

## Prerequisites and first-time setup

Run the commands below from the repository root. Development and validation require:

- .NET 10 SDK.
- Node.js 22 or later and npm (the reference tooling was tested with Node 24.22.0).
- Python 3 for the collection comparison script.
- Java capable of running ANTLR 4.9.3 to build the Node reference evaluator.
- The original `texttemplate-editor` checkout at `../texttemplate-editor`, or its
  location supplied through `TEXTTEMPLATE_LEGACY_ROOT`.

Install the pinned Node dependencies:

```bash
npm ci --ignore-scripts --prefix tools/node-reference
```

The reference build reads `Externals.ts`, its license, and Git provenance from the
adjacent checkout. Grammar files and the interpreter are preserved locally under
`reference/`. The build downloads a checksum-pinned ANTLR tool on first use; it
does not modify the reference checkout. Once built, the Node evaluator runs without
that checkout.

If Java is not on `PATH`, set `JAVA` to its executable. In the current Ubuntu
development environment:

```bash
export JAVA=/usr/share/elasticsearch/jdk/bin/java
```

## Generate and run C#

```bash
dotnet run --project src/TextTemplate.Compiler -- --root reference --output Generated
dotnet build Generated
dotnet run --no-build --project Generated -- \
  --template externals --data-file reference/data/externals.json
```

Generation refreshes the template sources and support code under `Generated/`.
Treat that directory as disposable compiler output: regeneration overwrites edits
to emitted files. Handwritten extensions live under `Extensions/` and are included
in the generated project, compiling into the same executable. Regeneration does
not overwrite those extension files.

## Full generation and validation workflow

After first-time setup, run the following sequence. There is currently no single
master script that wraps all these steps.

```bash
# Build the unchanged JavaScript reference interpreter and ANTLR-generated parser.
npm run build --prefix tools/node-reference

# Refresh and build the C# sources used for development.
dotnet run --project src/TextTemplate.Compiler -- --root reference --output Generated
dotnet build Generated

# Run Node reference regressions.
npm test --prefix tools/node-reference

# Run the complete C# suite, including independent Node/C# A/B cases.
dotnet test tests/TextTemplate.Tests --nologo

# Save the detailed comparison report for every reference template.
python3 tools/parity/compare.py --output /tmp/texttemplate-parity
```

The C# tests include focused language cases, standalone compilation, generated-code
readability, and extension behavior. A/B tests compare identical inputs against
the unchanged Node interpreter; they do not share semantic evaluation code.

`compare.py` generates and compiles a separate temporary C# project, including the
handwritten extensions. It runs every `reference/templates/*.template` in both
hosts under UTC with explicit matching input data. It compares raw stdout bytes
without whitespace or newline normalization. This script does **not** refresh the
workspace's `Generated/` directory; that is why generation is a separate step above.

The output directory contains `report.json`, each host's exact output, and separate
stderr diagnostics. The report records exit codes, byte counts, the first output
mismatch, input and extension hashes, and Node oracle provenance.

The latest verified collection result is **12 exact matches and 3 unavailable
external-resource cases**. `elastic`, `web`, and `wikipedia` require HTTP resources
that both offline hosts reject. Consequently, the comparison currently returns
**exit code 1**, even when every local template matches. Check `report.json` to
distinguish unavailable resources from mismatches or other execution failures;
do not treat all nonzero exits as expected. Matching failures are not output parity.

The C# suite also has an existing skipped computed-include annotation-scope case,
documented in [DESIGN.md section 11.3](DESIGN.md#113-known-compatibility-gap-found-during-fluent-boundary-testing).
Passing the available tests does not establish parity for every language feature.

## Run a single example in JavaScript

After building the reference evaluator:

```bash
node tools/node-reference/cli.mjs \
  --template externals --template-root reference \
  --data-file reference/data/externals.json
```

This demonstrates a custom method, a value function, and annotation scope. The
matching C# command is in the generation section above. Both hosts write rendered
text to stdout and diagnostics to stderr.

## Predicate counting example

`Count(predicate)` is exercised by a dedicated reference script, including list
filtering, singleton data, missing values, and caller arguments:

```bash
node tools/node-reference/cli.mjs --template countpredicate --template-root reference --data-file reference/data/countpredicate.json
dotnet run --project Generated -- --template countpredicate --data-file reference/data/countpredicate.json
```

Focused Node cases are in `tests/node-reference/count-predicate-cases.json` and are
also consumed by the C# A/B tests. Other unfinished features remain documented in
[DESIGN.md](DESIGN.md).

## Repository guide

| Location | Purpose |
| --- | --- |
| `reference/templates/`, `reference/subtemplates/`, `reference/data/` | Template collection, shared definitions, and input data |
| `reference/grammar/`, `reference/typescript/`, `reference/fixtures/` | Preserved legacy grammar, interpreter sources, and original JSON fixtures |
| `src/TextTemplate.Compiler/` | Handwritten parser and standalone C# emitter |
| `Generated/` | Generated template logic, registration, support code, and executable project |
| `Extensions/` | Handwritten C# method/value-function implementations and registration |
| `tools/node-reference/` | Build and CLI host for the legacy Node evaluator |
| `tools/parity/` | Reference-collection A/B comparison and reporting |
| `tests/` | C# tests, Node tests, characterized cases, and frozen outputs |

Further documentation:

- [Compiler commands and supported behavior](src/TextTemplate.Compiler/README.md)
- [Node evaluator setup and CLI contract](tools/node-reference/README.md)
- [Collection comparison and reports](tools/parity/README.md)
- [Writing and registering C# extensions](Extensions/README.md)
- [JavaScript Externals walkthrough](tools/node-reference/EXTERNALS.md)
- [Design, milestones, and known compatibility gaps](DESIGN.md)
- [Reference provenance](reference/README.md)
