using System.Text.RegularExpressions;

namespace TextTemplate.Compiler.Parsing;

// Hand-translated from the frozen TextTemplateLexer.g4 (Apache-2.0 reference).
// Match the longest rule; declaration order breaks ties, just as in ANTLR.
public sealed class TemplateLexer(SourceText source, bool sharedTemplate = false)
{
    private enum Mode { Text, Braced, Parened, Quoted, Apostrophed, Bracketed, Nested }
    private sealed record Rule(string Kind, Regex Pattern, bool Skip, Mode? Push, bool Pop, Mode? Switch);
    private static Rule R(string kind, string pattern, bool skip = false, Mode? push = null, bool pop = false, Mode? change = null)
        => new(kind, new Regex(@"\G(?:" + pattern + ")", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), skip, push, pop, change);
    private const string Block = @"/\*[\s\S]*?\*/";
    private const string Comment = @"[ \t]*//[^\n]*(?:\n[ \t]*//[^\n]*)*";
    private const string Continue = "[ \\t]*`[ \\t]*\\n[ \\t]*";
    private const string ContinueComment = "[ \\t]*`[ \\t]*//[^\\n]*(?:\\n[ \\t]*//[^\\n]*)*\\n[ \\t]*";
    private const string Id = @"[@$a-zA-Z_^*][a-zA-Z0-9_]*";
    private const string Method = @"\.[#@a-zA-Z_][a-zA-Z0-9_]*\(";
    private const string Relational = @"==|!=|=|<=|>=|<|>";
    private const string RegexEscape = @"\\[^\r\n\u2028\u2029]";
    private const string RegexClass = @"\[(?:[^\r\n\u2028\u2029\]\\]|" + RegexEscape + @")*\]";
    private const string RegexFirst = @"(?:[^*\r\n\u2028\u2029\\/\[]|" + RegexEscape + "|" + RegexClass + ")";
    private const string RegexNext = @"(?:[^\r\n\u2028\u2029\\/\[]|" + RegexEscape + "|" + RegexClass + ")";
    private static readonly Dictionary<Mode, Rule[]> Rules = new()
    {
        [Mode.Text] = [
            R("SLASH_STAR", Block, true), R("COMMENT_LINE", @"\n[ \t]*//[^\n]*", true),
            R("COMMENT_SKIP", Comment, true), R("CONTINUATION", Continue), R("CONTINUATION", ContinueComment),
            R("BULLET", @"[ \t]*\{\.\}"), R("TEXT", "[^{} /\\n`]+"), R("TEXT", "`"),
            R("LBRACE", @"\{", push: Mode.Braced), R("RBRACE", @"\}"), R("SPACES", " +"),
            R("TEXT", "/"), R("NL", "\n"), R("SUBTEMPLATES", @"[ \t\n]+Subtemplates:[ \t\n]+")
        ],
        [Mode.Braced] = [
            R("BRACED_SLASH_STAR", Block, true), R("LBRACKET", "\\[ *`[^\\n]*\\n", push: Mode.Bracketed),
            R("BRACED_COMMENT", @"//[^\n]*(?:\n|$)", true), R("IDENTIFIER", Id), R("METHODNAME", Method, push: Mode.Parened),
            R("DOT", @"\."), R("ARROW", "=>"), R("RELATIONAL", Relational), R("THINARROW", "->"),
            R("RBRACE", @"\}", pop: true), R("WS", @"[ \t\n]+", true), R("LP", @"\(", push: Mode.Nested),
            R("RP", @"\)"), R("COMMA", ","), R("COLON", ":"),
            R("LBRACKET", @"\[[ \t]*\n[ \t]*", push: Mode.Bracketed), R("LBRACKET", @"\[", push: Mode.Bracketed),
            R("QUOTE", "\"", push: Mode.Quoted), R("APOSTROPHE", "'", push: Mode.Apostrophed),
            R("AND", "&"), R("OR", @"\|"), R("NOT", "!"), R("POUND", "#"), R("MINUS", "-"), R("DIGITS", "[0-9]+"),
            R("BRACED_ILLEGAL", @"[%*\-={;<>?\\\]]+")
        ],
        [Mode.Parened] = [
            R("PARENED_SLASH_STAR", Block, true), R("REGEX", "/" + RegexFirst + RegexNext + "*/[gims]*"),
            R("PARENED_COMMENT", @" +//[^\n]*(?:\n|$)", true), R("PARENED_WS", @"[ \t\n]+", true),
            R("LBRACKET", @"\[", push: Mode.Bracketed), R("IDENTIFIER", Id), R("DOT", @"\."),
            R("METHODNAME", Method, push: Mode.Parened), R("RELATIONAL", Relational), R("DIGITS", "[0-9]+"), R("MINUS", "-"),
            R("RP", @"\)", pop: true), R("QUOTE", "\"", push: Mode.Quoted), R("APOSTROPHE", "'", push: Mode.Apostrophed),
            R("COMMA", ","), R("AND", "&"), R("OR", @"\|"), R("NOT", "!"), R("LP", @"\(", push: Mode.Nested),
            R("POUND", "#"), R("PARENED_ILLEGAL", @"\}")
        ],
        [Mode.Quoted] = [
            R("QUOTE", "\"", pop: true),
            R("TEXT", "(?:\\\\(?:[\"\\\\/bfnrt]|u[0-9a-fA-F]{4})|[^\"\\\\])+"),
            R("QUOTED_BAD_BACKSLASH", "\\\\[^\"\\\\/bfnrt]")
        ],
        [Mode.Apostrophed] = [
            R("TEXT", @"\\'"), R("TEXT", @"\\\\"), R("BAD_BACKSLASH", @"\\"),
            R("APOSTROPHE", "'", pop: true), R("TEXT", @"[^'\\]+")
        ],
        [Mode.Bracketed] = [
            R("BRACKETED_SLASH_STAR", Block, true), R("BRACKETED_COMMENT_LINE", @"\n[ \t]*//[^\n]*", true),
            R("BULLET", @"[ \t]*\{\.\}"), R("BRACKETED_COMMENT_SKIP", Comment, true),
            R("CONTINUATION", Continue), R("CONTINUATION", ContinueComment),
            R("RBRACKET", @"[ \t]*\n[ \t]*\]", pop: true), R("RBRACKETLP", @"\]\(", change: Mode.Parened),
            R("RBRACKET", @"\]", pop: true), R("TEXT", "[^{} /`\\n\\]]+"), R("TEXT", "`"),
            R("LBRACE", @"\{", push: Mode.Braced), R("RBRACE", @"\}"), R("SPACES", " +"), R("TEXT", "/"),
            R("NL", "\n"), R("SUBTEMPLATES", @"[ \t\n]+Subtemplates:[ \t\n]+")
        ],
        [Mode.Nested] = [
            R("NESTED_SLASH_STAR", Block, true), R("NESTED_COMMENT5", @" +//[^\n]*(?:\n|$)", true),
            R("IDENTIFIER", Id), R("METHODNAME", @"\.[a-zA-Z_][a-zA-Z0-9_]*\(", push: Mode.Parened),
            R("DOT", @"\."), R("RELATIONAL", Relational), R("NESTED_WS", @"[ \t\n]+", true),
            R("LP", @"\(", push: Mode.Nested), R("RP", @"\)", pop: true), R("AND", "&"), R("OR", @"\|"),
            R("NOT", "!"), R("DIGITS", "[0-9]+"), R("NESTED_QUOTE", "\"", push: Mode.Quoted),
            R("NESTED_APOSTROPHE", "'", push: Mode.Apostrophed), R("POUND", "#"), R("NESTED_ILLEGAL", @"\}")
        ]
    };

    public IReadOnlyList<SyntaxToken> Lex()
    {
        var result = new List<SyntaxToken>();
        var stack = new Stack<Mode>();
        Mode mode = sharedTemplate ? Mode.Braced : Mode.Text;
        int offset = 0;
        while (offset < source.Normalized.Length)
        {
            Rule? selected = null;
            int length = 0;
            foreach (var rule in Rules[mode])
            {
                var match = rule.Pattern.Match(source.Normalized, offset);
                if (match.Success && match.Index == offset && match.Length > length) { selected = rule; length = match.Length; }
            }
            if (selected is null) throw source.Error("Unrecognized character in " + mode + " context.", source.Span(offset, 1));
            var span = source.Span(offset, length);
            result.Add(new(selected.Kind, source.Normalized.Substring(offset, length), span, selected.Skip));
            offset += length;
            if (selected.Push is { } next) { stack.Push(mode); mode = next; }
            if (selected.Pop)
            {
                if (!stack.TryPop(out mode)) throw source.Error("Unexpected closing delimiter.", span);
            }
            if (selected.Switch is { } change) mode = change;
        }
        result.Add(new("EOF", "", source.Span(offset, 0)));
        return result;
    }
}
