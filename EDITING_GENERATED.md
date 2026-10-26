# Editing generated templates

Generate with `dotnet run --project src/TextTemplate.Compiler -- --root reference`.
After conversion, edit the C# under `Generated/Templates/` and rebuild with
`dotnet build Generated`. Regeneration replaces those edits.
The generated project includes its support sources and needs no TextTemplate assemblies.

Start at `Evaluate()` and the main rendering method immediately below it. Named
subtemplates and their helpers follow in original source order. Original template
fragments and source locations remain as comments. `Generated/Registration/` holds
scoped definition bindings and include adapters; ordinary text changes do not require
editing registration.

## Values and fluent calls

```csharp
TemplateValue type = Read("type");
TemplateValue name = Read("name");
return Line($"a {type} named {name}");
```

`Line` retains structured content and missing values; it does not add a newline.
Pass interpolated text directly to it. Assigning that interpolation to an ordinary
`string` first would stringify the values and lose template composition semantics.

The helpers deliberately distinguish lookup, method input, and rendered output:

| Helper | Meaning |
| --- | --- |
| `Read("name")` | Read a field from the current data, retaining its template value. |
| `Field("name")` | Collect a field across current items as input to a method chain. |
| `Collect("literal")` | Collect a constant in the current item contexts for a method chain. |
| `Subtemplate("tests")` | Evaluate a scoped subtemplate as input to a method chain. |
| `EvaluateSubtemplate("expected")` | Evaluate a scoped subtemplate using rendered-list semantics. |
| `Value(value)` | Wrap an existing compatibility value without rendering it. |

These operations differ on list contexts, missing values, and composition. Do not
replace one with another solely because they happen to agree for one scalar input.

```csharp
TemplateValue result = Subtemplate("tests")
    .Assert(() => [EvaluateSubtemplate("expected"), "ALL TESTS PASSED"]);
return Line($"{result}");
```

Method arguments use a callback returning a collection expression. This preserves
when arguments execute, including calls that adjust a multiline input before evaluating
the arguments. Arguments may be ordinary literals or `TemplateValue` instances.
A named call uses `.Call("anded")`; built-in operations use methods such as `.Join(...)`,
`.ToUpper()`, and `.ToDate(...)`. Date diagnostics use the C# caller file and line; original template locations stay in comments.

## Contexts and data pipelines

`ForItem(item => item.formatPet())` adapts a template operation to a supplied child
context. `InScope(item => item.formatPet())` additionally renders each current item.
Both inherit definitions and annotations; they do not mutate the caller's context.
The generic template base constructs the appropriate template instance internally.

`Where`, `GroupBy`, and `OrderBy` chain in execution order, followed by `Render`.
They call the verified TextTemplate algorithms, not LINQ equivalents. In generated
resource-loading pipelines, `contextProbe` and the `replay` callback preserve the
legacy distinction between evaluating an operation on a resource path and evaluating
it again after loading the data. Keep both paths consistent when modifying a pipeline.
Selector metadata retains inferred data keys or a computed-key marker, not the original template expression.

Complex choices are local functions with ordinary `if` statements. Their bodies run
only when called at the original expression position. Moving branch operations above
that call can change annotation effects, errors, and evaluation order.

## Debugging

Put breakpoints in named template methods or fluent-chain statements. Split a chain
into `TemplateValue` locals when you want to inspect individual stages. Small context
adapters use `DebuggerStepThrough` so Step Into normally passes through the adapter;
the underlying support code remains available for deliberate debugging.

Data and typed values show compact JSON from stored values. Parent links and other
implementation details are excluded; ordinary values are not shortened. Cycles,
missing values, callbacks, unsupported values, and excessive nesting use explanatory
fallback text rather than fabricated JSON. The display represents the current logical
data, which may already reflect legacy singleton-list normalization.

`TemplateContent.ToString()` still renders content. Its DebugView, and the display of a
`TemplateValue` containing content or a rendered list, preview already-evaluated fragments
as text. Preview uses an isolated rendering context, so it cannot advance live numbering.
The preview has no surrounding output context; final indentation or numbering may differ
when the value is embedded in a larger document. Cycles and callbacks are rejected before
rendering. Data has an explicit JSON-first DebugView; nested content appears as JSON text.
Hovering never executes deferred template code or loads resources. Automated tests cover
the preview strings; the IDE's exact hover presentation depends on its debugger.

`object?` remains within compatibility adapters, unfinished-feature stubs, and some
special operations such as `Case`. `TemplateValue.Raw` is the explicit boundary to
that layer. Routine formatting and fluent operations should use the typed surface.
