
# Legacy TextTemplate Reference Materials

This directory contains source files and test fixtures copied from
the original `eisnerw/texttemplate-editor` repository.

These files are preserved to document the original TextTemplate
syntax and behavior.

## Rules

- Do not modify the reference files when implementing the new compiler.
- Make changes in the appropriate `src/` projects instead.
- Preserve the original grammar until behavioral equivalence is achieved.
- Use the reference implementation and fixtures to develop A/B tests.
- Document any differences between the original and generated output.

## Original repository

https://github.com/eisnerw/texttemplate-editor

## C# development reference

The unchanged TypeScript interpreter running under Node.js is the authoritative
behavioral reference for the new C# generator. Develop C# template logic and
helper algorithms directly from the TypeScript implementation and validate them
against the Node.js runner using identical templates, subtemplates, and data.

There is no need to recover the old C# implementation or maintain a
`legacy-csharp/` directory. Generated programs must contain executable
template-specific C# logic and any necessary helpers as source, and run without
TextTemplate project assemblies.

See [the Node.js runner documentation](../tools/node-reference/README.md) for
commands, regression tests, and source provenance, and [DESIGN.md](../DESIGN.md)
for the development plan.

## Separate template files

- `templates/*.template`: one file per entry in `fixtures/templates.json`, plus `startup.template`.
- `subtemplates/*.template`: one file per entry in `fixtures/subtemplates.json`.
- `data/*.json`: one input value per named entry in `fixtures/data.json`.
- `fixtures/data.json`: original named data collection, retained unchanged.

File names preserve the original lookup names. Arrays of template lines are joined
with LF, exactly as in the legacy routes, without adding a final newline or
changing indentation. The original JSON fixtures are retained unchanged for the
Node reference runner and its recorded provenance. Inline subtemplate definitions
remain inside their owning templates; the subtemplates directory contains the
separately named shared subtemplates from the JSON collection.

The `data/` directory contains `nested.json`, `escapeTest.json`, `simpledata.json`,
`people.json`, `events.json`, `docs.json`, and `famousBirthdays.json`. Each file
contains the entry's object or array directly, without its outer lookup name.
Values, array order, and string contents are preserved; JSON is pretty-printed.
These files are derived copies, not replacements for the original fixture database.

`templates/startup.template` is extracted from the default `createEditor([...])` input
in `../texttemplate-editor/src/main/index.html`. It is the original startup page,
not an entry from the template JSON collection.

## Running templates

Run commands from the repository root. Templates are stored in `templates.json`;
`data.json` holds named input data sets, not template scripts.

| Source | Main template | Shared subtemplates | Named input data |
| --- | --- | --- | --- |
| Default JSON mode | `fixtures/templates.json` | `fixtures/subtemplates.json` | `fixtures/data.json` |
| `--template-root reference` | `templates/NAME.template` | `subtemplates/NAME.template` | `fixtures/data.json` |
| `--template-file FILE` | Explicit file | JSON by default; selected root with `--template-root` | Explicit `--data NAME` if needed |
| `--template-text TEXT` | Command-line text | JSON by default; selected root with `--template-root` | Explicit `--data NAME` if needed |

Named templates automatically receive same-named data when available, in either
collection mode. Explicit file/text inputs do not infer associated data or a
subtemplate directory. Use --template-root to select both file collections;
missing shared files never fall back to JSON in this mode.

```sh
# Original JSON template and associated input data
node tools/node-reference/cli.mjs --template simpledata
# Override input data with another named data set
node tools/node-reference/cli.mjs --template simpledata --data nested
# Both template and shared subtemplates from individual files
node tools/node-reference/cli.mjs --template simpledata --template-root reference
# Startup page
node tools/node-reference/cli.mjs --template startup --template-root reference
# Explicit main file with shared subtemplates from the same root
node tools/node-reference/cli.mjs --template-file reference/templates/simpledata.template --template-root reference --data simpledata
# Manual, from JSON or files
node tools/node-reference/cli.mjs --template docs
node tools/node-reference/cli.mjs --template docs --template-root reference
# Literal template text
node tools/node-reference/cli.mjs --template-text 'Hello {firstName}' --data nested
# Run the legacy assertions template from files
node tools/node-reference/cli.mjs --template tests --template-root reference
# Run the automated regression suite (JSON and file collections)
npm test --prefix tools/node-reference
```

Additional options:

| Option | Purpose |
| --- | --- |
| `--data-file FILE` | Use the JSON value in a file as input, instead of `--data NAME`; does not redirect named resource lookups. |
| `--fixtures DIRECTORY` | Select JSON collections; with `--template-root`, selects only the directory containing `data.json`. |
| `--debug-level 0/1/2` | Errors only (default), warnings, or full console debug on stderr. |
| `--status` | Show callback progress on stderr independently of debug level. |
| `--timeout-ms N` | Asynchronous completion timeout, default 10000 ms. |
| `--report-json` | Emit the structured result and resource trace instead of rendered text. |
| `--help` | Show usage. |

For example, to use a custom file collection and its data:

```sh
node tools/node-reference/cli.mjs --template example --template-root /path/to/project --fixtures /path/to/project/fixtures
```

That project must contain `templates/`, `subtemplates/`, and `fixtures/data.json`.
A standalone --data-file contains the actual input object/array, not the complete
map of named data sets. Both --data and --data-file override associated data.

See [the full CLI guide](../tools/node-reference/README.md#cli-contract) for setup,
all input combinations, output behavior, exit codes, and compatibility details.

## Parse the complete file collection with C#

```sh
dotnet run --project src/TextTemplate.Compiler -- --root reference --format json --output /tmp/texttemplate-trees.json
```

This produces one map of syntax trees for all `.template` files under `templates/`
and `subtemplates/`, including inline definitions, reference indexes, and original
source locations. A syntax error prevents JSON publication. It does not render
templates; use the Node runner commands above for rendered output. See the
[compiler guide](../src/TextTemplate.Compiler/README.md) for the JSON structure and
validation tests.

The compiler now generates standalone C# by default; use `--format json` for the
parse-tree map. For example:

```sh
dotnet run --project src/TextTemplate.Compiler -- --root reference --output /tmp/Generated.cs
```

The entire collection generates compilable C#, with runtime stubs for unfinished
execution features. See the [compiler guide](../src/TextTemplate.Compiler/README.md)
for building, supplying data, execution coverage, and stub behavior.

## Run generated C# with a data file

The generated executable accepts `--data-file FILE`:

```sh
dotnet run --project src/TextTemplate.Compiler -- --root reference --output Generated
dotnet build Generated/GeneratedTemplates.csproj
dotnet run --no-build --project Generated -- --template simpledata --data-file reference/data/nested.json
```

The template name and data filename are independent. Paths are relative to the
shell's working directory; no same-name data is selected automatically by generated
C#. Compilation does not embed the data files, so the same compiled template can
be run repeatedly with different inputs.

The command above now matches the Node output exactly:

```text
Hello Smith, John with a dog named missing --namex-- THIS IS AN INCLUDED SUBTEMPLATE!!
```

Compare it with the unchanged Node evaluator:

```sh
node tools/node-reference/cli.mjs --template simpledata --template-root reference --data-file reference/data/nested.json
```

Multi-element array contexts (for example `people.json`) also remain unfinished
in the generated runtime. These limitations are independent of file loading.
