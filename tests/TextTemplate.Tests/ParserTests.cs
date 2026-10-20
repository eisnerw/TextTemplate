using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using TextTemplate.Compiler;
using TextTemplate.Compiler.Parsing;

namespace TextTemplate.Tests;

public sealed class ParserTests
{
    private static readonly string Root = FindRoot();
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TextTemplate.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
    private static SyntaxNode Parse(string text, bool shared = false)
        => new TemplateParser(new SourceText("test.template", text), shared).Parse(shared);
    private static IEnumerable<SyntaxNode> Walk(SyntaxNode node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Walk)) yield return child;
    }
    private static object Projection(SyntaxNode node) => new
    {
        kind = node.Kind.ToString(), value = node.Value, children = node.Children.Select(Projection).ToArray()
    };
    private static JsonObject Oracle(Dictionary<string, object> inputs)
    {
        var info = new ProcessStartInfo("node")
        {
            WorkingDirectory = Root, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        info.ArgumentList.Add("tools/node-reference/parse-oracle.mjs");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(JsonSerializer.Serialize(inputs));
        process.StandardInput.Close();
        if (!process.WaitForExit(180000)) { process.Kill(true); throw new Exception("ANTLR oracle exceeded 180 seconds."); }
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult());
        return JsonNode.Parse(output.GetAwaiter().GetResult())!.AsObject();
    }
    private static void Compare(string name, TemplateParser parser, SyntaxNode tree, JsonNode oracle)
    {
        Assert.Empty(oracle["errors"]!.AsArray());
        var tokens = parser.AllTokens.Where(t => !t.IsTrivia && t.Kind != "EOF").Select(t => new { kind = t.Kind, text = t.Text });
        Assert.True(JsonNode.DeepEquals(JsonSerializer.SerializeToNode(tokens), oracle["tokens"]), "Token mismatch: " + name);
        var actual = JsonSerializer.SerializeToNode(Projection(tree));
        Assert.True(JsonNode.DeepEquals(actual, oracle["tree"]), "Tree mismatch: " + name + " at " + FirstDifference(actual, oracle["tree"], "tree"));
    }
    private static string FirstDifference(JsonNode? a, JsonNode? b, string path)
    {
        if (JsonNode.DeepEquals(a, b)) return "";
        if (a is JsonObject ao && b is JsonObject bo)
            foreach (var key in ao.Select(k => k.Key).Union(bo.Select(k => k.Key)))
            { var diff = FirstDifference(ao[key], bo[key], path + "." + key); if (diff != "") return diff; }
        if (a is JsonArray aa && b is JsonArray ba)
        {
            if (aa.Count != ba.Count) return path + ".length";
            for (int i = 0; i < aa.Count; i++) { var diff = FirstDifference(aa[i], ba[i], path + "[" + i + "]"); if (diff != "") return diff; }
        }
        return path;
    }

    [Fact]
    public void EntireReferenceDirectoryMatchesAntlrTokensAndTrees()
    {
        string directory = Path.Combine(Root, "reference");
        var result = new TextTemplateCompiler().CompileDirectory(directory);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Equal(Directory.GetFiles(Path.Combine(directory, "templates"), "*.template").Length +
                     Directory.GetFiles(Path.Combine(directory, "subtemplates"), "*.template").Length, result.Document!.Sources.Count);
        var inputs = result.Document.Sources.ToDictionary(p => p.Key, p => (object)new { text = p.Value.Text, category = p.Value.Category });
        var oracle = Oracle(inputs);
        foreach (var (key, value) in result.Document.Sources)
        {
            var parser = new TemplateParser(new SourceText(key, value.Text), value.Category == "subtemplates");
            Compare(key, parser, value.Tree, oracle[key]!);
            foreach (var node in Walk(value.Tree))
                Assert.InRange(node.Span.Offset + node.Span.Length, 0, value.Text.Length);
        }
        var simple = result.Document.Sources["templates/simple.template"];
        Assert.Contains(simple.Definitions, d => d.Name == "formatPet");
        Assert.Contains(simple.References, d => d.Name == "formatPet");
        var include = result.Document.Sources["templates/simpledata.template"];
        Assert.Contains(include.References, d => d.Name == "doInclude");
    }

    [Fact]
    public void FocusedGrammarCasesMatchAntlr()
    {
        string[] cases = [
            "Hello {firstName}!", "{a|b&c}", "{!a=b|c}", "{(a=b)|!c}", "{a=>[yes],[no]}", "{a->[yes]}",
            "{people:[{name}],['fallback']}", "{:['implicit']}", "{name.[Hello {$0}]('x')}",
            "{value.Replace(/[a-z\\/]+/gi,'x').ToUpper()}", "{value.Foo(--12,13)}",
            "{value.Foo(1 2)}", "{'a\\'b\\\\c'}", "{\"a\\u1234\\n\"}",
            "a\r\n\tb\n{value /*comment*/ .ToUpper()}", "// first\nhello`\n world",
            "{.} one\n  {.} two", "{[\n  hello\n ]}", "{[  `comment\nhello]}",
            "{:[]}", "main\nSubtemplates:\n{#a:[{#b}\nSubtemplates:\n{#b:[nested]}]}",
            "{a.@Include('#external').#local()}", "{x.Foo(/* a */1, // b\n 2)}",
            "literal 😀 {name}", "{^.^.$1 = 2->[yes]}"
        ];
        var input = cases.Select((text, i) => (text, key: i.ToString())).ToDictionary(p => p.key, p => (object)new { p.text, category = "templates" });
        var oracle = Oracle(input);
        for (int i = 0; i < cases.Length; i++)
        {
            var parser = new TemplateParser(new SourceText(i.ToString(), cases[i]));
            Compare(i.ToString(), parser, parser.Parse(), oracle[i.ToString()]!);
        }
    }

    [Fact]
    public void OriginalTextAndLocationsSurviveNormalization()
    {
        const string text = "a\r\n\t{name}";
        var tree = Parse(text);
        var identifier = Walk(tree).Single(n => n.Kind == SyntaxKind.Identifier);
        Assert.Equal(new SourceSpan(5, 4, 2, 3), identifier.Span);
        Assert.Equal("name", text.Substring(identifier.Span.Offset, identifier.Span.Length));
        Assert.Equal(text.Length, tree.Span.Length);
        var parser = new TemplateParser(new SourceText("comments", "a/* original comment */{name}"));
        Assert.Contains(parser.AllTokens, t => t.IsTrivia && t.Text == "/* original comment */");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{name")]
    [InlineData("{name.Foo(}")]
    [InlineData("{name:[unterminated}")]
    [InlineData("{\"unterminated}")]
    [InlineData("{a=}")]
    [InlineData("{a &}")]
    [InlineData("{1}")]
    public void SyntaxFailuresHaveOriginalLocationAndExcerpt(string text)
    {
        var exception = Assert.Throws<SyntaxException>(() => Parse(text));
        Assert.Equal("test.template", exception.Diagnostic.Source);
        Assert.NotEmpty(exception.Diagnostic.Message);
        Assert.Equal(text, exception.Diagnostic.Excerpt);
        Assert.InRange(exception.Diagnostic.Span.Offset, 0, text.Length);
    }

    [Fact]
    public void DirectoryMapIsDeterministicAndFailureDoesNotReplaceOutput()
    {
        string dir = Path.Combine(Path.GetTempPath(), "texttemplate-parser-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "templates", "nested"));
            Directory.CreateDirectory(Path.Combine(dir, "subtemplates"));
            File.WriteAllText(Path.Combine(dir, "templates", "nested", "same.template"), "Hello {#same}");
            File.WriteAllText(Path.Combine(dir, "subtemplates", "same.template"), "[World]");
            File.WriteAllText(Path.Combine(dir, "templates", "ignored.txt"), "{");
            var compiler = new TextTemplateCompiler();
            var first = compiler.CompileDirectory(dir);
            Assert.True(first.Success);
            Assert.Equal(new[] { "subtemplates/same.template", "templates/nested/same.template" }, first.Document!.Sources.Keys);
            string output = Path.Combine(dir, "trees.json");
            Assert.Equal(0, Program.Main(["--root", dir, "--format", "json", "--output", output]));
            byte[] before = File.ReadAllBytes(output);
            TextTemplateCompiler.WriteJson(compiler.CompileDirectory(dir).Document!, output);
            Assert.Equal(before, File.ReadAllBytes(output));
            File.WriteAllText(Path.Combine(dir, "templates", "bad.template"), "{");
            File.WriteAllText(Path.Combine(dir, "subtemplates", "bad.template"), "[unterminated");
            var failed = compiler.CompileDirectory(dir);
            Assert.False(failed.Success); Assert.Null(failed.Document); Assert.Equal(2, failed.Diagnostics.Count);
            Assert.Equal(1, Program.Main(["--root", dir, "--format", "json", "--output", output]));
            Assert.Equal(before, File.ReadAllBytes(output));
            string absent = Path.Combine(dir, "absent.json");
            Assert.Equal(1, Program.Main(["--root", dir, "--format", "json", "--output", absent]));
            Assert.False(File.Exists(absent));
        }
        finally { Directory.Delete(dir, true); }
    }
}
