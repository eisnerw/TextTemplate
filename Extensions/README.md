# Handwritten C# extensions

These files compile into the generated executable. They are not generated from
TypeScript and regeneration does not overwrite them. No TextTemplate assembly,
JavaScript engine, parser, or ANTLR visitor is required at runtime.

## Run the example

From the repository root:

```sh
dotnet run --project src/TextTemplate.Compiler -- --root reference --output Generated
dotnet run --project Generated -- --template externals --data-file reference/data/externals.json
```

`Externals.cs` implements the two legacy examples. `ExtensionRegistration.cs`
registers their template names independently of their C# method names:

```csharp
extensions
    .AddMethod("test", TextTemplateExtensions.Externals.WrapUpper)
    .AddValueFunction("test", TextTemplateExtensions.Externals.PrefixString);
```

The generated project includes `../Extensions/**/*.cs` by default. To use a
separate source directory, build with:

```sh
dotnet build Generated -p:TemplateExtensionsDirectory=/absolute/path/to/extensions
```

Keep that source directory alongside the generated project when moving it, or
supply the property at the destination. The dependency is C# source only.
Single-file output contains the same API; compile your extension sources alongside
it. Without a registration implementation, the optional partial hook does nothing.

## Add a method

Write ordinary C# in this directory, then add a registration:

```csharp
public static TemplateValue Surround(ExtensionCall call)
{
    call.RequireArgumentCount(2);
    string prefix = call.Arguments.Evaluate(0).Render();
    string suffix = call.Arguments.Evaluate(1).Render();
    return TemplateValue.From(prefix + call.Receiver.Render() + suffix);
}
```

Register with `.AddMethod("Surround", Surround)` and use
`{name.Surround('<', '>')}`. Rebuild the executable after changing C#.

Names are case sensitive. Duplicate registrations in the same namespace throw.
Methods and value functions have separate namespaces. Ordinary method registrations
are consulted before built-ins, including `Case`, `Where`, `GroupBy`, `OrderBy`,
and named subtemplate methods (registered with a leading `#`). Annotation overrides
(names starting with `@`) are deliberately rejected; this API supports methods and
value functions, not arbitrary mutation of annotation state.

Each argument is deferred separately. `Evaluate(index)` executes it in its captured
caller scope, with inherited annotations, on every call. It is not cached. An
extension may ignore an argument, evaluate it once, or evaluate it repeatedly.
Use `call.Context.Lookup(path)` for an additional lookup in the calling context.
Neither debugger inspection nor `ToString()` evaluates deferred arguments.

`TemplateValue` retains structured values. Returning `call.Receiver`, an evaluated
argument, or `lookup.DefaultValue()` preserves that structure. `TryGetString` tests
the actual value without flattening it. `Render()` explicitly composes text without
resolving final bullet numbering; the enclosing renderer retains that responsibility.
`ToJavaScriptString()` provides legacy JavaScript coercion, including regex spelling
and object/null behavior. `RenderWithMissingValues()` exposes the legacy missing-value
messages used by the `test` example. `Uppercase()` uses the verified Node-compatible
casing tables. `ToString()` is a side-effect-free diagnostic summary, not rendering.

`RequireArgumentCount` throws with the handwritten C# location. For legacy
recoverable errors, call `ReportError` and return the desired fallback value. The
example preserves the legacy rule of evaluating arguments before reporting an
invalid count. The CLI writes diagnostics to stderr, retains the rendered result
on stdout, and returns 1. Other thrown exceptions stop execution.

## Add a value function

```csharp
public static TemplateValue Prefix(ValueLookup lookup)
{
    TemplateValue value = lookup.DefaultValue();
    return value.TryGetString(out string text)
        ? TemplateValue.From(">>" + text)
        : value;
}
```

Register it with `.AddValueFunction("prefix", Prefix)` and select it with
`{[{name}].@ValueFunction('prefix')}`. `lookup.Name` identifies the requested path.
`DefaultValue()` performs normal lookup without invoking the value function again;
no temporary mutation or visitor manipulation is necessary. Child scopes inherit
the function; `@ValueFunction()` clears it only within its scope. An unknown name
reports an error and clears the function, matching the characterized legacy behavior.

## Embed the generated code

Callers can supply their own registry instead of the default partial registration:

```csharp
var extensions = new TemplateExtensions()
    .AddMethod("Surround", Surround)
    .AddValueFunction("prefix", Prefix);
string text = GeneratedTemplates.Render("example", data.RootElement,
    extensions: extensions, reportError: message => Console.Error.WriteLine(message));
```

Registration is copied at the start of each render and shared by its child contexts.
There is no global mutable registry. Finish registration before using it from
concurrent callers; extension delegates must manage any mutable state they capture.
If no error callback is supplied, a reported extension error throws instead of
being silently ignored.

## Verification

`externals.template` and `externals-cases.json` compare the handwritten examples
with the unchanged Node interpreter, including scope, Unicode casing, regex
arguments, missing values, and diagnostic fallback output. Additional C# tests
verify custom names without parser changes, built-in precedence, deferred/repeated
argument evaluation, registration isolation, and preservation during regeneration.
