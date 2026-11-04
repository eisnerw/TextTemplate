# Proposed generated-runtime diagnostics

This is a design proposal, not an implemented API. Log and @Debug still have
runtime stubs. The generated runtime should expose a small callback contract;
a host can adapt it to Microsoft.Extensions.Logging without making that package
or any TextTemplate assembly a dependency of generated code.

## Suggested contract

Add an optional diagnostics sink to GeneratedTemplates.Render, after the existing
parameters so current callers remain valid. Emit these types as support source:

```csharp
public enum TemplateDiagnosticKind { Error, Log, MissingValue, Decision, Context, Call, Value }
public sealed record TemplateDiagnostic(
    TemplateDiagnosticKind Kind,
    int DetailLevel,
    string Message,
    string File,
    int Line);
```

The sink is `Action<TemplateDiagnostic>?`. A host can route errors to LogError,
explicit Log calls to LogInformation, and trace events to LogDebug/LogTrace.
The legacy numeric detail level is not a logging severity: both explicit Log
messages and errors use level zero in the interpreter. Preserve the event kind
separately. No diagnostic may be appended to rendered text.

Keep the existing reportError callback during migration. Specify that a new sink
observes errors but does not by itself change the existing throw/continue policy;
otherwise attaching a logger would unexpectedly change template execution.
Sink failures should propagate as host failures rather than silently disappear.

## Legacy behavior to preserve and test

The reference is TextTemplateInterpreter.ts, callMethod(Log/@Debug),
logForDebug, and its call sites.

- Log() observes its receiver and returns the receiver unchanged.
- Log(argument) evaluates the argument in caller scope, observes that value,
  and still returns the original receiver. Log has special argument evaluation;
  do not route it through eager evaluation of every supplied argument.
- Extra arguments report an error; characterize precisely which operands run.
- Missing values, structured content, data, booleans, dates, and multiline values
  have distinct log representations. Rendering a diagnostic must not advance the
  live bullet/numbering state. Some legacy serialization is lossy or surprising;
  freeze observed examples before choosing corresponding C# representations.
- @Debug takes exactly one JavaScript parseInt-compatible argument. The source
  checks NaN but does not restrict it to 0 through 7. Preserve that behavior unless
  a deliberate language change is approved.
- Its threshold is scoped and inherited like other annotations. A message at
  level N is included unless the current threshold is less than N.
- Legacy levels are 0 errors/explicit logs, 1 missing values, 2 decisions,
  3 context changes, 4 named-template calls, 5 values, 6 methods, 7 unnamed calls.
- External method overrides must retain priority and control argument evaluation.

## C# authoring and CLI behavior

Use CallerFilePath/CallerLineNumber to identify maintained C# call sites; do not
reintroduce original template expressions as executable metadata. Do not reproduce
ANTLR visitor class names or editor hover-position events.

The CLI should write diagnostics to stderr. A structured diagnostic option can
emit one JSON event per line there, leaving stdout as exact rendered output.
Explicit Log calls should be visible without enabling detailed tracing. Trace
threshold defaults to zero; expose a separate CLI option for the initial level.

Debugger ToString/DebugView remains a separate, side-effect-free facility. Looking
at a value in the debugger must never invoke the sink, evaluate Log arguments,
resolve resources, or change rendering state. Ordinary template execution still
must evaluate explicit Log operands even if no host sink is attached.

## Implementation sequence

1. Capture Node report-json debugLog events and rendered output for Log arguments,
   missing/data/date values, extra arguments, and inherited @Debug scopes.
2. Add the generated callback contract and explicit Log behavior, including CLI
   stderr output and tests that rendered stdout is unchanged.
3. Add scoped @Debug and incrementally instrument the existing runtime operations.
4. Test event order and level filtering independently from rendered-output A/B
   parity. Document any intentional C# diagnostic wording differences.
