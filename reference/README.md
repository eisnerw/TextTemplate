
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

## Legacy C# implementation

The `legacy-csharp/` directory is reserved for the original C#
ANTLR visitor and supporting implementation files, when recovered.

The original C# implementation is intended to serve as the primary
behavioral reference for the new generator.
