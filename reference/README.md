
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
