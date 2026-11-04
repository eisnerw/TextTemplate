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

The build honors `JAVA` or `JAVA_HOME`; otherwise it checks `PATH` and the
current environment's `/usr/share/elasticsearch/jdk/bin/java`. To override discovery:

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

After first-time setup, run the complete workflow with:

```bash
python3 tools/validate.py
```

This builds the Node oracle, regenerates and builds `Generated/`, runs both test
suites, and compares every reference template. Reports go to `artifacts/validation`
(or `--output DIRECTORY`). Mismatches and test failures fail the run.

The equivalent individual steps are:

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

The active collection contains 20 templates. The web-only demos `elastic`, `web`,
and `wikipedia` are archived under `reference/templates/Unimplemented/` and excluded
from normal generation and comparison. They are outside the standalone scope.

The previously skipped computed-include annotation-scope case is now enabled and
covered by additional regressions; see [DESIGN.md section 11.3](DESIGN.md#113-known-compatibility-gap-found-during-fluent-boundary-testing).
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

## Index, Join, date and regex examples

Four additional reference templates exercise the implemented forms: `indexing`,
`joining`, `dateforms`, and `regexforms`. For example:

```bash
node tools/node-reference/cli.mjs --template indexing --template-root reference --data-file reference/data/indexing.json
dotnet run --project Generated -- --template indexing --data-file reference/data/indexing.json
```

Substitute another example name in both the template and data arguments. Regenerate
`Generated/` first after changing the compiler. The focused A/B regressions are:

```bash
dotnet test tests/TextTemplate.Tests --filter 'DisplayName~index|DisplayName~join-extended|DisplayName~date-extended|DisplayName~regex-extended'
```

`Index()` reports an item's one-based position; `Index(start, count)` slices data.
The examples also cover Join separators, week-based dates, ISO ordinal/week/basic
inputs, and JavaScript regex escapes and capture behavior. Legacy quirks are retained,
including singleton collapse and the dictionary `Index(2)` behavior.

Additional examples are `dateparsing`, `regexnames`, `includescopes`, and
`replacevalues`; each has a matching file in `reference/data/`. They demonstrate
expanded date inputs and date encoding, capture-name forms, computed include
scope, and non-string replacement operands.

See [COMPLETION.md](COMPLETION.md) for the current implementation checklist,
remaining execution gaps, and the deferred diagnostics, ToJson, and resource-hosting
decisions. This increment does not remove every runtime stub.

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
