using System.Text.Json.Serialization;

namespace TextTemplate.Compiler.Parsing;

// Offsets and lengths count UTF-16 code units; lines and columns are one-based.
public sealed record SourceSpan(int Offset, int Length, int Line, int Column);
public enum SyntaxKind
{
    Template, SharedTemplate, Text, Whitespace, NewLine, Continuation, Bullet,
    Interpolation, Context, ContextWithFallback, SubtemplateSection,
    Identifier, NamedSubtemplate, String, Number, Regex, InlineTemplate,
    Call, InlineCall, Arguments, Group, Not, Relational, Logical, Arrow, ThinArrow
}
public sealed record SyntaxNode(SyntaxKind Kind, SourceSpan Span, string? Value, IReadOnlyList<SyntaxNode> Children);
public sealed record SyntaxToken(string Kind, string Text, SourceSpan Span, bool IsTrivia = false);
public sealed record Diagnostic(string Source, string Message, SourceSpan Span, string Excerpt);
public sealed record SymbolOccurrence(string Name, SourceSpan Span);
public sealed record ParsedSource(string Name, string Category, string Text, SyntaxNode Tree,
    IReadOnlyList<SyntaxToken> Tokens, IReadOnlyList<SymbolOccurrence> Definitions,
    IReadOnlyList<SymbolOccurrence> References);
public sealed record CompilationDocument(int SchemaVersion, string OffsetEncoding, SortedDictionary<string, ParsedSource> Sources);
public sealed record CompilationResult(CompilationDocument? Document, IReadOnlyList<Diagnostic> Diagnostics)
{
    [JsonIgnore] public bool Success => Document is not null && Diagnostics.Count == 0;
}
public sealed class SyntaxException(Diagnostic diagnostic) : Exception(diagnostic.Message)
{
    public Diagnostic Diagnostic { get; } = diagnostic;
}
