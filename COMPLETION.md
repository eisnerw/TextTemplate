# Compatibility completion checklist

This is the current checklist. DESIGN.md also contains historical implementation
notes; older lists of stubs there may have been superseded. A feature is complete
only for its characterized cases, not merely because a stub was removed.

## Implemented and covered by Node/C# regression cases

- [x] Standalone, readable C# generation and handwritten C# extensions.
- [x] Existing local reference collection and structured composition.
- [x] Count predicates, Index positions/slices, and Join continuation behavior.
- [x] Computed include-name annotation scope, including definitions exported by
  an include and method calls in selected/unselected argument branches.
- [x] Practical date input additions: compact, ordinal, and week timestamps;
  characterized RFC zones/offsets; common English and slash-separated dates.
- [x] Week/year/era formatting and characterized daylight-saving behavior.
- [x] Non-string Replace searches: JavaScript coercion, first-match replacement,
  and substitution tokens. String searches retain legacy split/join behavior.
- [x] Regex forward references, legacy escapes, capture resets, escaped and
  supplementary capture names, and duplicate names in separate alternatives.
- [x] Recognized-date EncodeFor(uri/json); html/xml remain invalid operations,
  matching the legacy requirement for a string receiver.
- [x] One validation command: `python3 tools/validate.py`.

## Dates: complete within the agreed scope

Date support is complete for the characterized scalar forms. Unsupported parsing
forms, year zero, dates outside the supported calendar range, and non-scalar
inputs fail explicitly with `NotImplementedException` and a `NOTYETIMPLEMENTED`
message. These edge cases are excluded from the completion backlog; the marker
is an error, not successful rendered template output. Invalid supported inputs
continue to report invalid-date errors.

## Execution coverage still to finish

- [ ] Dynamic annotation arguments still restricted by the emitter, including
  multiline/bullet styles, missing-value replacements, and falsy patterns.
- [ ] Additional include-unit layouts and include chains that still emit stubs.
- [ ] Too-short Case forms, non-object contexts, and remaining non-scalar
  composition paths. Separate valid legacy behavior from genuine input errors.
- [ ] Audit generic emitter fallbacks against the frozen grammar and documented
  methods. Unknown unregistered extension methods should remain explicit errors.

## Design work

- [ ] Log and @Debug: implement the proposed host-supplied callback in
  [DIAGNOSTICS.md](DIAGNOSTICS.md). Keep
  rendered output separate; specify levels, scope, deferred evaluation, and
  diagnostic location using maintained C# sources. Debug inspection must remain
  side-effect-free. Do not claim editor debug traces are already reproduced.
- [ ] ToJson: resolve legacy forms that expose original template source against
  the rule that original source belongs only in generated comments.

## Intentionally deferred

- Inline-template calls: the attempted argument-taking form fails in legacy.
  Use named templates; revisit only with a working legacy example or an explicit
  language-extension decision.

## Completion criteria

- All working, in-scope legacy features have independent Node/C# A/B coverage.
- No valid in-scope path reaches an unfinished runtime stub.
- The full validation command passes for the active reference collection.
- Generated code remains understandable and independently compilable, and public
  documentation lists intentional exclusions and behavioral quirks accurately.
