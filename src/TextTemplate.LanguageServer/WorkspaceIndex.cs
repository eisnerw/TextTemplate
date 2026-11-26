using TextTemplate.Compiler.Parsing;

internal sealed class WorkspaceIndex(string workspaceRoot, IReadOnlyDictionary<string, string> openDocuments)
{
    private sealed class Scope(Scope? parent)
    {
        internal Scope? Parent { get; } = parent;
    }

    private sealed record SourceDocument(string Uri, string Category, SourceText Source, SyntaxNode Tree,
        IReadOnlyList<SyntaxToken> Tokens);
    private sealed record Definition(string Name, string Uri, SourceSpan Span, Scope? Scope, bool SharedFile);
    private sealed record Reference(string Name, SourceSpan Span, Scope Scope);

    internal object[] FindDefinitions(string documentUri, string text, int line, int character)
    {
        string? documentPath = FilePath(documentUri);
        if (documentPath is null
            || !TryFindTemplateRoot(workspaceRoot, documentPath, out string templateRoot, out string currentCategory)) return [];

        SourceDocument? current = Parse(templateRoot, documentUri, documentPath, currentCategory, text);
        if (current is null) return [];
        int offset = OffsetAt(text, line, character);
        var references = new List<Reference>();
        var definitions = new List<Definition>();
        CollectSymbols(current, new Scope(null), references, definitions);
        Reference? reference = references.FirstOrDefault(candidate =>
            offset >= candidate.Span.Offset && offset <= candidate.Span.Offset + candidate.Span.Length);
        if (reference is null) return [];

        var sources = ReadWorkspaceSources(templateRoot, documentUri, documentPath, currentCategory, text);
        foreach (SourceDocument source in sources)
        {
            if (source.Uri == documentUri) continue;
            var sourceDefinitions = new List<Definition>();
            CollectSymbols(source, new Scope(null), new List<Reference>(), sourceDefinitions);
            definitions.AddRange(sourceDefinitions.Where(definition => definition.SharedFile));
        }

        var localMatches = new List<Definition>();
        for (Scope? scope = reference.Scope; scope is not null; scope = scope.Parent)
        {
            localMatches = definitions.Where(definition =>
                !definition.SharedFile
                && definition.Uri == documentUri
                && ReferenceEquals(definition.Scope, scope)
                && definition.Name == reference.Name).ToList();
            if (localMatches.Count > 0) break;
        }

        List<Definition> matches = localMatches.Count > 0
            ? localMatches
            : definitions.Where(definition => definition.SharedFile && definition.Name == reference.Name).ToList();

        return matches
            .Select(definition => ToLocation(definition, sources, current))
            .Where(location => location is not null)
            .Cast<object>()
            .ToArray();
    }

    private List<SourceDocument> ReadWorkspaceSources(string templateRoot, string currentUri, string currentPath, string currentCategory, string currentText)
    {
        var texts = new Dictionary<string, (string Uri, string Category, string Text)>(PathComparer());
        foreach (string category in new[] { "templates", "subtemplates" })
        {
            string directory = Path.Combine(templateRoot, category);
            if (!Directory.Exists(directory)) continue;
            foreach (string file in Directory.EnumerateFiles(directory, "*.template", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                if (category == "templates" && relative.StartsWith("Unimplemented/", StringComparison.Ordinal)) continue;
                string fullPath = Path.GetFullPath(file);
                string uri = new Uri(fullPath).AbsoluteUri;
                string fileText = File.ReadAllText(fullPath);
                texts[fullPath] = (uri, category, fileText);
            }
        }

        foreach ((string openUri, string openText) in openDocuments)
        {
            string? path = FilePath(openUri);
            if (path is null || !TryFindTemplateRoot(templateRoot, path, out string openRoot, out string category)
                || !PathComparer().Equals(openRoot, templateRoot)) continue;
            texts[Path.GetFullPath(path)] = (openUri, category, openText);
        }

        texts[Path.GetFullPath(currentPath)] = (currentUri, currentCategory, currentText);
        return texts
            .Select(entry => Parse(templateRoot, entry.Value.Uri, entry.Key, entry.Value.Category, entry.Value.Text))
            .Where(source => source is not null)
            .Cast<SourceDocument>()
            .ToList();
    }

    private static SourceDocument? Parse(string templateRoot, string uri, string path, string category, string text)
    {
        try
        {
            string relative = Path.GetRelativePath(templateRoot, path).Replace('\\', '/');
            bool shared = category == "subtemplates";
            var source = new SourceText(relative, text);
            var parser = new TemplateParser(source, shared);
            return new(uri, category, source, parser.Parse(shared), parser.AllTokens);
        }
        catch (SyntaxException)
        {
            return null;
        }
    }

    private static void CollectSymbols(SourceDocument source, Scope rootScope,
        List<Reference> references, List<Definition> definitions)
    {
        if (source.Category == "subtemplates")
        {
            string? path = FilePath(source.Uri);
            if (path is not null)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                definitions.Add(new(name, source.Uri, source.Source.OriginalSpan(0, source.Source.Original.Length), null, true));
            }
        }

        Visit(source.Tree, rootScope);

        void Visit(SyntaxNode node, Scope scope)
        {
            if (node.Kind == SyntaxKind.SubtemplateSection)
            {
                foreach (SyntaxNode child in node.Children)
                {
                    if (child.Kind == SyntaxKind.Context && child.Value == "explicit"
                        && child.Children.Count > 1 && child.Children[0].Kind == SyntaxKind.NamedSubtemplate)
                    {
                        definitions.Add(new(child.Children[0].Value!, source.Uri, child.Children[0].Span, scope, false));
                        var childScope = new Scope(scope);
                        foreach (SyntaxNode body in child.Children.Skip(1)) Visit(body, childScope);
                    }
                    else Visit(child, scope);
                }
                return;
            }

            if (node.Kind == SyntaxKind.NamedSubtemplate)
                references.Add(new(node.Value!, node.Span, scope));

            if (node.Kind == SyntaxKind.Call && node.Value is { } callName && callName.StartsWith('#'))
            {
                SyntaxToken? methodToken = source.Tokens.FirstOrDefault(token =>
                    token.Kind == "METHODNAME"
                    && token.Span.Offset >= node.Span.Offset
                    && token.Span.Offset < node.Span.Offset + node.Span.Length
                    && token.Text.StartsWith("." + callName + "(", StringComparison.Ordinal));
                if (methodToken is not null)
                    references.Add(new(callName[1..], methodToken.Span, scope));
            }

            if (node.Kind == SyntaxKind.Call && node.Value == "@Include")
            {
                foreach (SyntaxNode argument in node.Children.Skip(1).SelectMany(group => group.Children))
                {
                    if (argument.Kind != SyntaxKind.String || argument.Value is not { Length: > 2 } literal
                        || literal[1] != '#') continue;
                    SourceSpan span = source.Source.OriginalSpan(argument.Span.Offset + 1, literal.Length - 2);
                    references.Add(new(literal[2..^1], span, scope));
                }
            }

            foreach (SyntaxNode child in node.Children) Visit(child, scope);
        }
    }

    private object? ToLocation(Definition definition, IReadOnlyList<SourceDocument> sources, SourceDocument current)
    {
        SourceDocument? source = definition.Uri == current.Uri
            ? current
            : sources.FirstOrDefault(candidate => candidate.Uri == definition.Uri);
        if (source is null) return null;
        var span = definition.Span;
        return new
        {
            uri = definition.Uri,
            range = new
            {
                start = PositionAt(source.Source.Original, span.Offset),
                end = PositionAt(source.Source.Original, span.Offset + span.Length)
            }
        };
    }

    internal static bool TryFindTemplateRoot(string workspaceRoot, string path, out string templateRoot, out string category)
    {
        string fullRoot = Path.GetFullPath(workspaceRoot);
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        while (directory is not null && IsWithinRoot(fullRoot, directory))
        {
            string name = Path.GetFileName(directory);
            if (name is "templates" or "subtemplates")
            {
                string? candidate = Directory.GetParent(directory)?.FullName;
                string sibling = name == "templates" ? "subtemplates" : "templates";
                if (candidate is not null && IsWithinRoot(fullRoot, candidate)
                    && Directory.Exists(Path.Combine(candidate, sibling)))
                {
                    templateRoot = candidate;
                    category = name;
                    return true;
                }
            }
            if (PathComparer().Equals(directory, fullRoot)) break;
            directory = Directory.GetParent(directory)?.FullName;
        }

        templateRoot = "";
        category = "";
        return false;
    }

    private static bool IsWithinRoot(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string? FilePath(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

    private static int OffsetAt(string text, int line, int character)
    {
        int currentLine = 0;
        int offset = 0;
        while (offset < text.Length && currentLine < line)
        {
            if (text[offset++] == '\n') currentLine++;
        }
        int lineEnd = text.IndexOf('\n', offset);
        if (lineEnd < 0) lineEnd = text.Length;
        return Math.Min(offset + Math.Max(0, character), lineEnd);
    }

    private static object PositionAt(string text, int offset)
    {
        int line = 0;
        int character = 0;
        for (int index = 0; index < Math.Clamp(offset, 0, text.Length); index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < offset && text[index + 1] == '\n') index++;
                line++;
                character = 0;
            }
            else if (text[index] == '\n')
            {
                line++;
                character = 0;
            }
            else character++;
        }
        return new { line, character };
    }

    private static StringComparer PathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}