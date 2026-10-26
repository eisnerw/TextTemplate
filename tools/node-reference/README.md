# Node.js legacy reference evaluator

This is Milestone 1's executable A-side oracle. It calls the preserved TypeScript
interpreter directly; it contains no C# implementation or replacement evaluation logic.

See [the Externals example](EXTERNALS.md) for custom methods, value functions,
annotation scope, observed JavaScript output, and the pending C# compatibility gap.

## Build and run

Requires Node.js 22 or later, npm, and a Java runtime capable of running ANTLR 4.9.3.
The tested environment is Node.js 24.22.0 on Linux ARM64.

From the repository root:

```sh
npm ci --ignore-scripts --prefix tools/node-reference
npm run build --prefix tools/node-reference
node tools/node-reference/cli.mjs --template simple
npm test --prefix tools/node-reference
```

If Java is not on PATH, set JAVA to its executable. In this environment:

```sh
JAVA=/usr/share/elasticsearch/jdk/bin/java npm run build --prefix tools/node-reference
```

The build downloads the checksum-pinned ANTLR 4.9.3 tool on first use, generates
JavaScript from unchanged grammar copies, and transpiles without semantic edits.
Generated files stay in ignored .build/. The legacy grammar emits two warnings
about lexer rules that can match empty text; these are preserved.

The adjacent ../texttemplate-editor checkout supplies its original TypeScript
Externals.ts and Apache-2.0 LICENSE.txt. Override its location with
TEXTTEMPLATE_LEGACY_ROOT. No compiled artifacts or configuration are copied from
that checkout. The build copies its license into .build/LICENSE.legacy.txt.
After building, execution uses only the local build, installed dependencies, and
selected fixtures; the adjacent checkout is not required at runtime.

Versions are intentionally pinned: ANTLR tool 4.9.3, JavaScript runtime 4.11.0,
Moment 2.29.4, TypeScript transpiler 5.6.3. The first three reflect the legacy
build/lockfile. The original tool/runtime pairing passes the regression suite.

## CLI contract

Choose exactly one input:

- --template NAME: resolve a template from templates.json, or from
  templates/NAME.template when --template-root is supplied. Pass the name without
  the .template extension.
- --template-file FILE: read exact UTF-8 template text.
- --template-text TEXT: evaluate the argument as template text.

Optional arguments:

- --data NAME: resolve data from data.json.
- --data-file FILE: read a JSON value as the input data, not a collection of
  named data sets. This overrides associated data but does not change the source
  of /data/NAME resource lookups.
- --fixtures DIRECTORY: directory containing templates.json, subtemplates.json,
  and data.json. Defaults to this repository's reference/fixtures, independently
  of the current working directory. With --template-root, only data.json is
  loaded from this directory; the two template JSON files are not required.
- --template-root DIRECTORY: load named templates from DIRECTORY/templates/*.template
  and shared subtemplates from DIRECTORY/subtemplates/*.template. Both folders
  must exist (they may be empty). Uses regular files directly in each folder,
  preserving their UTF-8 text without trimming. No fallback to template JSON
  occurs when a name is missing. This option works with all three input forms.
- --debug-level 0|1|2: control legacy console output on stderr. Level 0
  (default) shows errors only; 1 adds warnings; 2 adds debug messages such as
  missing-value and resource-loading traces. This is host console verbosity,
  separate from the template language’s scoped @Debug annotation. It does not
  change evaluation or the debugLog field in JSON reports.
- --status: emit callback progress messages to stderr, independently of debug level.
- --timeout-ms N: fail if asynchronous evaluation does not complete within N
  milliseconds (default 10000). This event-loop timer cannot interrupt synchronous
  interpreter execution; callers needing a hard deadline should use a subprocess
  timeout, as the tests do.
- --report-json: emit the original result fields, callback event types, and resolved
  resource trace as JSON instead of rendered text. This preserves null and boolean
  results for future A/B comparison.
- --help: print usage.

Explicit data overrides associated template data. With neither data option, a
named template receives same-named data when the legacy route would supply it,
in either JSON or file-collection mode. --template-file and --template-text do
not infer a data name: supply --data or --data-file when needed. --template-file
also does not infer a sibling subtemplates folder; select it with --template-root.
Explicit relative paths are resolved against the working directory.
Each CLI invocation is a fresh process: URL, parse-tree, and subtemplate caches
cannot leak across cases.

Default stdout is UTF-8 result text with no added newline. Null/undefined results
write zero bytes; boolean and numeric results use JavaScript string conversion,
matching browser text assignment. Strings are neither HTML-escaped nor trimmed.
Enabled legacy console messages, interpreter diagnostics, and host errors go to stderr.
Error diagnostics remain visible at every debug level (including legacy lexer
errors emitted through console.log).
Thrown initial interpreter exceptions retain the worker's "EXCEPTION " plus stack
result and are also diagnosed on stderr. Stack paths/line numbers are environment
dependent and are not frozen as portable golden text.

Exit codes:

- 0: completed without reported errors.
- 1: interpreter errors, a missing-resource error object, or an initial interpreter exception.
- 2: usage, filesystem, unsupported resource, callback/host failure, timeout,
  or an unexpected non-scalar result.

A completed result is retained even when exit code is 1. The legacy Assert method
can render "ASSERT FAILURE" with no errors; this remains result text and exit 0.
In particular, the legacy tests template returns "ALL TESTS PASSED", while the
separate #doit subtemplate has an observed test-8 mismatch. Both are preserved.

## Commands for each input mode

Run these commands from the repository root. The fixture files have different roles:
`templates.json` contains template scripts, `subtemplates.json` contains shared
scripts, and `data.json` contains named input data sets.

### Original JSON collections (default)

```sh
# Named template with its associated data, if present
node tools/node-reference/cli.mjs --template simpledata
# Manual
node tools/node-reference/cli.mjs --template docs
# Override associated data with another named input
node tools/node-reference/cli.mjs --template simpledata --data nested
# Use different JSON collections
node tools/node-reference/cli.mjs --template simple --fixtures /path/to/fixtures
```

### Separate template and subtemplate files

The checked-in layout is:

```text
reference/
  templates/
    startup.template
    simpledata.template
    tests.template
    ...
  subtemplates/
    xformatPet.template
    doInclude.template
    ...
  fixtures/
    data.json
```

```sh
# File collections, with automatic same-named data lookup
node tools/node-reference/cli.mjs --template simpledata --template-root reference
# Startup page (only present in the file collection)
node tools/node-reference/cli.mjs --template startup --template-root reference
# Explicit main file, shared subtemplates from the same root, explicit data
node tools/node-reference/cli.mjs --template-file reference/templates/simpledata.template --template-root reference --data simpledata
# Manual from its individual file
node tools/node-reference/cli.mjs --template-file reference/templates/docs.template --template-root reference
# Legacy assertions using file collections: expected output ALL TESTS PASSED
node tools/node-reference/cli.mjs --template tests --template-root reference
```

For a custom root with the same layout, use both selectors:

```sh
node tools/node-reference/cli.mjs --template example --template-root /path/to/project --fixtures /path/to/project/fixtures
```

Shared references such as #xformatPet and @Include('#doInclude') resolve against
that root's subtemplates folder, including further shared references made by a
loaded subtemplate. Definitions written inside a template stay in that template
and retain the interpreter's existing scoping rules. Missing files produce the
legacy missing-entry diagnostics; the host does not substitute a JSON definition.

### Individual text/file input with JSON shared subtemplates

Omitting --template-root deliberately keeps shared subtemplate lookups in JSON:

```sh
node tools/node-reference/cli.mjs --template-file reference/templates/simpledata.template --data simpledata
node tools/node-reference/cli.mjs --template-text 'Hello {firstName}' --data nested
```

To use a standalone input-data file instead of a named data set:

```sh
node tools/node-reference/cli.mjs --template-file reference/templates/simpledata.template --template-root reference --data-file /path/to/person.json
```

Here person.json contains the actual input object (for example, fields firstName,
lastName, and pets), not a map keyed by a data-set name. Any /data/NAME requests
inside the template still use the selected fixture directory's data.json.

### Output, diagnostics, and regression tests

```sh
# Save exact rendered output
node tools/node-reference/cli.mjs --template startup --template-root reference > /tmp/startup-output.txt
# Show console debug and callback status on stderr
node tools/node-reference/cli.mjs --template simpledata --template-root reference --debug-level 2 --status
# Inspect the result and actual resource responses as JSON
node tools/node-reference/cli.mjs --template simpledata --template-root reference --report-json
# Full automated suite, including file-collection comparisons against goldens
npm test --prefix tools/node-reference
# Show all CLI switches
node tools/node-reference/cli.mjs --help
```

The assertions template is a single legacy test script, not the whole automated
suite. External-service templates still require unsupported network resources;
selecting file collections does not enable HTTP access.

## Callback and lookup compatibility

interpret(input, callback, options) emits status, url, and result events.
A url event contains path and success(data), not a failure continuation.
The adapter schedules success on setImmediate, after the initial call unwinds;
the interpreter itself caches the result and reevaluates. Multiple fetch rounds
remain under interpreter control. There is no HTTP, Express server, browser, or
browser Worker in this host.

Only /template/NAME, /subtemplate/NAME, and /data/NAME are mapped. Names are URI
decoded. Template and subtemplate arrays join with LF; data arrays remain arrays.
Lookups retain legacy truthiness checks, missing-entry error messages, associated
template data, and the /template/data fallback. Error objects are delivered through
success, just as successful HTTP responses containing missing-entry errors were.

Responses follow ParserFacade's decoded AJAX values, including its truthy Result
wrapper CR-removal behavior. runonserver.js instead delivers raw response text;
that transport is not the chosen baseline. General external URLs and arbitrary
filesystem paths inside resource requests are unsupported. Fixtures requiring
external services are outside this filesystem-only milestone.

The interpreter itself removes CR and expands tabs to four spaces before parsing;
the host does not normalize the input. Empty-string resource responses can leave
the original interpreter waiting indefinitely. The host times out rather than
inventing an answer.

ParserFacade appends fetched subtemplates to the editor using /*SHARED*/ markers;
the editor's save action separates those definitions for storage. The interpreter
already parses fetched subtemplates (including nested definitions and @Include).
A regression test appends the actual browser-form definitions to simpledata and
verifies identical output without further fetches. No additional preprocessing is
needed for this verified fixture. Supplied shared definitions remain untouched;
this test is not proof of equivalence for every editor interaction or fetch order.

## Regression evidence and provenance

tests/node-reference/cases.json records exact CLI inputs. golden/*.json records
observed result values, diagnostics, callback traces, resource responses, and exit
codes. They are captured from this Node-hosted unchanged interpreter, not claimed
as independently recorded browser outputs. Independent anchors include the
legacy tests template's own assertions, the simple template's explicit expected
text, and direct route-contract tests.

The suite runs each case in separate processes under TZ=UTC and LANG=C.UTF-8,
compares both structured reports and exact stdout, and reports the first differing
UTF-16 character with escaped surrounding text and inputs. It covers nested data,
bullets, whitespace, includes, associated data, missing values/resources, syntax
and regex errors, scalar results, asynchronous delivery, and timeouts.

golden/provenance.json records the original Git revision, SHA-256 hashes of the
interpreter, grammar, Externals.ts, and all fixture files, plus tool versions.
Tests verify those hashes and build provenance. The original source revision is
ccedefd87b012ff1173ee2f43323b6eb7c8bd268. Intentional baseline updates should be
reviewed with their inputs and source hashes; tests never rewrite goldens.

The copied parsingTest.js/lexingTest.js concern an older calculation language and
are not used as TextTemplate acceptance tests. Browser UI queuing, hover behavior,
external network fixtures and locale/date-dependent output remain outside the
verified baseline. This TypeScript evaluator is the behavioral reference for C#
development; no recovered C# implementation is required.

Focused `include-cases.json` and `list-cases.json` characterizations live under
`tests/node-reference/`. List cases include the input data and exact expected text
for empty/singleton/multiple items, nested contexts, parent links, missing values,
indentation, and method/Join behavior. Both the Node tests and the independent
standalone C# tests consume those inputs; `npm test` includes these cases.
