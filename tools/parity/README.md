# Reference collection comparison

From the repository root, after building the Node reference tools:

```sh
python3 tools/parity/compare.py --output /tmp/texttemplate-parity
```

Requires Python 3, Node.js and .NET 10. The tool generates and builds all templates
in a temporary standalone project, with no TextTemplate assembly references. It
runs every unchanged `reference/templates/*.template` in both hosts under UTC.
The current file templates differ from the frozen JSON template collection; the
comparison intentionally reads the file collection on both sides.
Both receive the same explicit initial input: the same-named `reference/data`
file when present, otherwise `{}`. Shared templates come from
`reference/subtemplates`; named data comes from the unchanged JSON fixture
collection on Node and its equivalent individual files on C#.

The output directory contains exact stdout, stderr, and `report.json`, including
exit codes, byte lengths, first differing byte and escaped surrounding text,
source/input hashes, and Node oracle provenance. Temporary build files are removed.
There is no whitespace or newline normalization. Exit status is 0 only when every
template matches; mismatches, execution failures, timeouts, and unavailable external
resources return 1. Build failures abort with diagnostics.

Current result: **10 exact matches; 3 external-resource cases**. `web`, `wikipedia`,
and `elastic` require HTTP resources that both offline hosts reject. Matching
failure categories are recorded separately and are not rendering parity. The tool
does not make network requests or change those templates.

Frozen outputs and the evaluation-input manifest are in
`tests/node-reference/collection-golden/`. `LocalReferenceTemplatesMatchNode`
builds an independent executable and checks all ten local templates against both
the live Node oracle and these goldens. The legacy `tests` template deliberately
renders `ALL TESTS PASSED`; byte equality with that result is tested too.

```sh
dotnet test tests/TextTemplate.Tests --filter LocalReferenceTemplatesMatchNode
```

New templates are automatically included in the comparison. A new local template
also needs a reviewed Node golden before its collection regression can pass.
