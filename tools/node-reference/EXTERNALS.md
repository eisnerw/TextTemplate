# Externals reference example

Run the unchanged JavaScript interpreter with the new template and explicit data:

```sh
node tools/node-reference/cli.mjs --template externals --template-root reference \
  --data-file reference/data/externals.json
```

Explicit data is necessary here: Node's automatic associated-data lookup uses the
original JSON fixture collection, which does not contain this new example.

Observed output (including a final LF):

```text
Normal lookup: Alice
External method: <ALICE>
Structured receiver: <HELLO ALICE>
Evaluated arguments: <ALICE>
Value function: >>Alice
Non-string lookup: 42
Combined: <>>ALICE>
Inherited scope: outer=>>Alice; inner=>>Alice; cleared=Alice; restored=>>Alice
After scope: Alice
```

## How JavaScript invokes the extensions

The preserved interpreter imports `Externals` from the generated JavaScript module.
Its method dispatch checks the external lookup before evaluating built-in arguments:

```javascript
let externalMethod = Externals.getMethod(method);
if (externalMethod) {
    return externalMethod(value, args, this);
}
```

The existing `getMethod('test')` returns `testMethod`. That function receives the
receiver, unevaluated ANTLR argument context, and interpreter visitor. It composes a
non-string receiver with `visitor.compose(value, 2)`, evaluates arguments with
`accept(visitor)`, preserves regular expressions, and composes other arguments with
mode 0. With exactly two arguments it returns:

```javascript
return argValues[0] + value.toUpperCase() + argValues[1];
```

Thus `{name.test('<', '>')}` yields `<ALICE>`, and a structured receiver such as
`[Hello {name}]` is composed before uppercasing. Arguments can themselves be data
lookups, as shown by `name.test(prefix, suffix)`.

`@ValueFunction('test')` selects `Externals.getValueFunction('test')`. Its current
implementation is:

```javascript
function testFunction(ctx, visitor) {
    let value = visitor.visitIdentifier(ctx);
    if (typeof value == 'string') {
        value = '>>' + value;
    }
    return value;
}
```

The interpreter temporarily removes the active value function while invoking it,
so `visitIdentifier` performs ordinary lookup without recursively calling the
extension. It restores the hook afterward. The example verifies string changes,
unchanged numeric values, inheritance into nested blocks, clearing the hook with
`@ValueFunction()`, and restoration outside the nested and outer scopes.

Both lookups currently recognize only the exact name `test`; other names return
null. Adding a new extension means adding a function and a lookup branch in the
maintained `Externals.ts`, then rebuilding. There is no registration API. Do not
edit `.build/Externals.js`: it is disposable transpiled output. Currently the build
reads `Externals.ts` from the adjacent reference checkout; this example does not
modify that read-only source.

The example uses the existing extensions, not new implementations. It does not
exercise overriding a built-in because the preserved Externals defines no such
override. Method precedence is established by the dispatch code above.

## Regression coverage and C# status

`tests/node-reference/externals.test.mjs` checks exact output, structured results,
input hashes, and build provenance against `golden/externals.json`. Separate cases
verify too few/many method arguments and an unknown value-function name: the
legacy host reports an error, returns `Alice`, and exits 1. The successful output
is also frozen in `collection-golden/externals.txt`.

Run all Node regressions with `npm test --prefix tools/node-reference`.

The handwritten C# implementations in `Extensions/` now match this template and
the additional cases in `externals-cases.json`. The collection comparison includes
those C# sources and does not skip the example. See
[the C# extension guide](../../Extensions/README.md) for registration, deferred
arguments, value lookup, and standalone deployment.
