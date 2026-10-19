namespace TextTemplate.Compiler.Parsing;

// Recursive descent over the unchanged grammar. Nodes retain structure, not evaluated values.
public sealed class TemplateParser
{
    private readonly SourceText source;
    private readonly SyntaxToken[] tokens;
    private int position;
    private int depth;
    public IReadOnlyList<SyntaxToken> AllTokens { get; }
    private SyntaxToken Current => tokens[position];
    private string At => Current.Kind;

    public TemplateParser(SourceText source, bool sharedTemplate = false)
    {
        this.source = source;
        AllTokens = new TemplateLexer(source, sharedTemplate).Lex();
        tokens = AllTokens.Where(t => !t.IsTrivia).ToArray();
    }

    public SyntaxNode Parse(bool sharedTemplate = false)
    {
        var children = sharedTemplate ? new List<SyntaxNode> { Option() } : Contents("EOF");
        if (!sharedTemplate && children.Count == 0) throw Error("Expected template content.");
        Take("EOF");
        return new(sharedTemplate ? SyntaxKind.SharedTemplate : SyntaxKind.Template,
            source.OriginalSpan(0, source.Original.Length), null, children);
    }

    private SyntaxException Error(string message) => source.Error(message, Current.Span);
    private SyntaxToken Take(string expected)
    {
        if (At != expected) throw Error($"Expected {expected}, encountered {At}.");
        var token = Current;
        if (At != "EOF") position++;
        return token;
    }
    private SyntaxNode Node(SyntaxKind kind, int start, string? value, params SyntaxNode[] children)
    {
        int begin = tokens[start].Span.Offset;
        int end = position > start ? tokens[position - 1].Span.Offset + tokens[position - 1].Span.Length : begin;
        return new(kind, source.OriginalSpan(begin, end - begin), value, children);
    }
    private SyntaxNode Join(SyntaxKind kind, SyntaxNode left, string? value, params SyntaxNode[] rest)
    {
        int end = tokens[position - 1].Span.Offset + tokens[position - 1].Span.Length;
        return new(kind, source.OriginalSpan(left.Span.Offset, end - left.Span.Offset), value, new[] { left }.Concat(rest).ToArray());
    }
    private static bool IsText(string kind) => kind is "TEXT" or "SPACES" or "NL" or "CONTINUATION";
    private SyntaxNode Text()
    {
        int start = position;
        var t = Current; position++;
        var kind = t.Kind switch { "TEXT" => SyntaxKind.Text, "SPACES" => SyntaxKind.Whitespace,
            "NL" => SyntaxKind.NewLine, "CONTINUATION" => SyntaxKind.Continuation, "BULLET" => SyntaxKind.Bullet,
            _ => throw Error("Expected text.") };
        return Node(kind, start, t.Text);
    }
    private List<SyntaxNode> Contents(string end)
    {
        if (++depth > 256) throw Error("Template nesting exceeds the supported limit of 256.");
        try
        {
            var nodes = new List<SyntaxNode>();
            while (At != end && !(end == "RBRACKET" && At == "RBRACKETLP"))
            {
                if (IsText(At) || At == "BULLET") nodes.Add(Text());
                else if (At == "LBRACE") nodes.Add(Braces());
                else if (At == "SUBTEMPLATES") nodes.Add(Section());
                else throw Error($"Expected template content or {end}, encountered {At}.");
            }
            return nodes;
        }
        finally { depth--; }
    }
    private SyntaxNode Section()
    {
        int start = position; Take("SUBTEMPLATES");
        var children = new List<SyntaxNode>();
        while (IsText(At) || At == "LBRACE")
        {
            if (IsText(At)) children.Add(Text());
            else
            {
                var definition = Braces();
                if (definition.Kind != SyntaxKind.Context)
                    throw source.Error("Expected a subtemplate definition such as {#name:[...] }.", definition.Span);
                children.Add(definition);
            }
        }
        return Node(SyntaxKind.SubtemplateSection, start, null, children.ToArray());
    }
    private SyntaxNode Braces()
    {
        int start = position; Take("LBRACE");
        SyntaxNode? first = At == "COLON" ? null : Expression();
        if (At == "COLON")
        {
            if (first is not null) RequireOption(first);
            Take("COLON");
            var children = new List<SyntaxNode>();
            if (first is not null) children.Add(first);
            children.Add(Option());
            var kind = SyntaxKind.Context;
            if (At == "COMMA") { Take("COMMA"); children.Add(Option()); kind = SyntaxKind.ContextWithFallback; }
            Take("RBRACE");
            return Node(kind, start, first is null ? "implicit" : "explicit", children.ToArray());
        }
        if (first is null) throw Error("Expected an expression.");
        if (At is "ARROW" or "THINARROW")
        {
            RequirePredicate(first);
            bool thin = At == "THINARROW"; var op = Current.Text; position++;
            var children = new List<SyntaxNode> { first, Option() };
            if (!thin && At == "COMMA") { Take("COMMA"); children.Add(Option()); }
            first = Node(thin ? SyntaxKind.ThinArrow : SyntaxKind.Arrow, start + 1, op, children.ToArray());
        }
        else if (!IsOption(first) && !IsPredicate(first)) throw source.Error("Expected a template expression.", first.Span);
        Take("RBRACE");
        return Node(SyntaxKind.Interpolation, start, null, first);
    }
    private static bool IsOption(SyntaxNode n) => n.Kind is SyntaxKind.Identifier or SyntaxKind.NamedSubtemplate or SyntaxKind.String or SyntaxKind.InlineTemplate or SyntaxKind.Call or SyntaxKind.InlineCall;
    private static bool IsPredicate(SyntaxNode n) => n.Kind is SyntaxKind.Identifier or SyntaxKind.NamedSubtemplate or SyntaxKind.Call or SyntaxKind.InlineCall or SyntaxKind.Group or SyntaxKind.Not or SyntaxKind.Relational or SyntaxKind.Logical;
    private void RequireOption(SyntaxNode n)
    {
        if (!IsOption(n)) throw source.Error("Expected an identifier, string, template, or method invocation.", n.Span);
    }
    private void RequirePredicate(SyntaxNode n)
    {
        if (!IsPredicate(n)) throw source.Error("Expected a condition.", n.Span);
    }
    private SyntaxNode Option()
    {
        var node = Primary(); RequireOption(node); return node;
    }
    private SyntaxNode Expression()
    {
        var left = PredicatePart();
        // AND and OR share precedence and associate left, per predicateExpression.
        while (At is "AND" or "OR")
        {
            RequirePredicate(left);
            var op = Current.Text; position++;
            var right = PredicatePart(); RequirePredicate(right);
            left = Join(SyntaxKind.Logical, left, op, right);
        }
        return left;
    }
    private SyntaxNode PredicatePart()
    {
        if (++depth > 256) throw Error("Expression nesting exceeds the supported limit of 256.");
        try
        {
            int start = position;
            if (At == "NOT")
            {
                Take("NOT"); var child = PredicatePart(); RequirePredicate(child);
                return Node(SyntaxKind.Not, start, "!", child);
            }
            if (At == "LP")
            {
                Take("LP"); var child = Expression(); RequirePredicate(child); Take("RP");
                return Node(SyntaxKind.Group, start, null, child);
            }
            var left = Primary();
            if (At == "RELATIONAL")
            {
                var op = Take("RELATIONAL").Text;
                var right = Primary();
                return Join(SyntaxKind.Relational, left, op, right);
            }
            return left;
        }
        finally { depth--; }
    }
    private SyntaxNode Primary()
    {
        int start = position;
        SyntaxNode node;
        switch (At)
        {
            case "IDENTIFIER":
            case "TEXT":
                string name = Current.Text; position++;
                while (At == "DOT" && position + 1 < tokens.Length && tokens[position + 1].Kind is "IDENTIFIER" or "TEXT")
                {
                    Take("DOT"); name += "." + Current.Text; position++;
                }
                node = Node(SyntaxKind.Identifier, start, name); break;
            case "POUND":
                Take("POUND"); node = Node(SyntaxKind.NamedSubtemplate, start, Take("IDENTIFIER").Text); break;
            case "QUOTE":
            case "APOSTROPHE":
                var delimiter = At; var literal = Current.Text; position++;
                while (At == "TEXT") { literal += Take("TEXT").Text; }
                literal += Take(delimiter).Text;
                node = Node(SyntaxKind.String, start, literal); break;
            case "MINUS":
            case "DIGITS":
                string number = "";
                while (At == "MINUS") number += Take("MINUS").Text;
                number += Take("DIGITS").Text;
                return Node(SyntaxKind.Number, start, number);
            case "LBRACKET":
                node = Inline(false); break;
            default: throw Error("Expected an expression, encountered " + At + ".");
        }
        while (At is "METHODNAME" or "DOT")
        {
            var children = new List<SyntaxNode> { node };
            string? method = null;
            var kind = SyntaxKind.Call;
            if (At == "METHODNAME") method = Take("METHODNAME").Text[1..^1];
            else { Take("DOT"); children.Add(Inline(true)); kind = SyntaxKind.InlineCall; }
            while (At != "RP") children.Add(Arguments());
            Take("RP");
            node = Join(kind, node, method, children.Skip(1).ToArray());
        }
        return node;
    }
    private SyntaxNode Inline(bool invoked)
    {
        int start = position; Take("LBRACKET");
        var children = Contents("RBRACKET");
        Take(invoked ? "RBRACKETLP" : "RBRACKET");
        return Node(SyntaxKind.InlineTemplate, start, null, children.ToArray());
    }
    private SyntaxNode Arguments()
    {
        int start = position;
        var children = new List<SyntaxNode> { Argument() };
        while (At == "COMMA") { Take("COMMA"); children.Add(Argument()); }
        return Node(SyntaxKind.Arguments, start, null, children.ToArray());
    }
    private SyntaxNode Argument()
    {
        if (At == "REGEX") { int start = position; return NodeAfterRegex(start); }
        return Expression();
    }
    private SyntaxNode NodeAfterRegex(int start)
    {
        var text = Take("REGEX").Text;
        return Node(SyntaxKind.Regex, start, text);
    }
}
