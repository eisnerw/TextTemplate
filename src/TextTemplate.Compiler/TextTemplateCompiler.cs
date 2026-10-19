using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TextTemplate.Compiler.Parsing;

namespace TextTemplate.Compiler;

public sealed class TextTemplateCompiler
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 1024
    };

    public CompilationResult CompileDirectory(string root)
    {
        root = Path.GetFullPath(root);
        var sources = new SortedDictionary<string, ParsedSource>(StringComparer.Ordinal);
        var diagnostics = new List<Diagnostic>();
        foreach (string category in new[] { "templates", "subtemplates" })
        {
            string directory = Path.Combine(root, category);
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("Required directory not found: " + directory);
            foreach (string file in Directory.EnumerateFiles(directory, "*.template", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string key = Path.GetRelativePath(root, file).Replace('\\', '/');
                string name = Path.GetRelativePath(directory, file).Replace('\\', '/')[..^".template".Length];
                string text = File.ReadAllText(file, new UTF8Encoding(false, true));
                try
                {
                    bool shared = category == "subtemplates";
                    var parser = new TemplateParser(new SourceText(key, text), shared);
                    var tree = parser.Parse(shared);
                    var definitions = new List<SymbolOccurrence>();
                    var references = new List<SymbolOccurrence>();
                    Collect(tree, definitions, references);
                    sources.Add(key, new(name, category, text, tree, parser.AllTokens, definitions, references));
                }
                catch (SyntaxException error) { diagnostics.Add(error.Diagnostic); }
            }
        }
        return new(diagnostics.Count == 0 ? new(1, "UTF-16", sources) : null, diagnostics);
    }

    private static void Collect(SyntaxNode node, List<SymbolOccurrence> definitions, List<SymbolOccurrence> references)
    {
        if (node.Kind == SyntaxKind.SubtemplateSection)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind == SyntaxKind.Context && child.Value == "explicit" && child.Children[0].Kind == SyntaxKind.NamedSubtemplate)
                {
                    var name = child.Children[0];
                    definitions.Add(new(name.Value!, name.Span));
                    foreach (var body in child.Children.Skip(1)) Collect(body, definitions, references);
                }
                else Collect(child, definitions, references);
            }
            return;
        }
        if (node.Kind == SyntaxKind.NamedSubtemplate) references.Add(new(node.Value!, node.Span));
        if (node.Kind == SyntaxKind.Call && node.Value!.StartsWith('#')) references.Add(new(node.Value[1..], node.Span));
        // Includes can be computed. Only literal includes are indexed here; the tree retains all arguments.
        if (node.Kind == SyntaxKind.Call && node.Value == "@Include")
            foreach (var arg in node.Children.Skip(1).SelectMany(n => n.Children))
                if (arg.Kind == SyntaxKind.String && arg.Value is { Length: > 3 } literal && literal[1] == '#')
                    references.Add(new(literal[2..^1], arg.Span));
        foreach (var child in node.Children) Collect(child, definitions, references);
    }

    public static void WriteJson(CompilationDocument document, string output)
    {
        string fullPath = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions) + "\n", new UTF8Encoding(false));
            File.Move(temporary, fullPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
