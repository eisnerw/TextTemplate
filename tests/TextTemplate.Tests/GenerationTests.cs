using System.Diagnostics;
using System.Text.Json;
using TextTemplate.Compiler;
using TextTemplate.Compiler.Generation;

namespace TextTemplate.Tests;

public sealed class GenerationTests
{
    private static string Repository()
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "TextTemplate.slnx"))) path = path.Parent;
        return path?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
    private static (int Exit, string Output, string Error) Run(string file, string directory, params string[] arguments)
    {
        var info = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        info.Environment["TZ"] = "UTC";
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new Exception("Process timed out: " + file); }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
    private static void Match(string expected, string actual, string name)
    {
        if (expected == actual) return;
        int i = 0; while (i < Math.Min(expected.Length, actual.Length) && expected[i] == actual[i]) i++;
        Assert.Fail($"{name}: first mismatch at {i}; expected {JsonSerializer.Serialize(expected[Math.Max(0, i - 25)..Math.Min(expected.Length, i + 65)])}; actual {JsonSerializer.Serialize(actual[Math.Max(0, i - 25)..Math.Min(actual.Length, i + 65)])}");
    }

    [Theory]
    [InlineData("list")]
    [InlineData("regex")]
    [InlineData("resource")]
    [InlineData("annotation")]
    [InlineData("ifmissing")]
    [InlineData("count")]
    [InlineData("named-data")]
    [InlineData("switch")]
    [InlineData("multiline")]
    [InlineData("bullet")]
    public void RuntimeCasesMatchNode(string suite)
    {
        string root = Path.Combine(Path.GetTempPath(), "texttemplate-lists-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "templates"));
        Directory.CreateDirectory(Path.Combine(root, "subtemplates"));
        try
        {
            using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository(), "tests/node-reference/" + suite + "-cases.json")));
            foreach (var item in cases.RootElement.EnumerateArray())
                File.WriteAllText(Path.Combine(root, "templates", item.GetProperty("name").GetString() + ".template"), item.GetProperty("script").GetString());
            var parsed = new TextTemplateCompiler().CompileDirectory(root);
            Assert.True(parsed.Success, string.Join("\n", parsed.Diagnostics));
            var generated = new CSharpEmitter().Generate(parsed.Document!);
            Assert.True(generated.Success, string.Join("\n", generated.Diagnostics));
            foreach (var file in generated.Files)
            {
                string path = Path.Combine(root, "generated", file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Value);
            }
            File.WriteAllText(Path.Combine(root, "generated", "GeneratedTemplates.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>""");
            var build = Run("dotnet", root, "build", "generated", "-o", "output", "--nologo");
            Assert.True(build.Exit == 0, build.Output + build.Error);
            foreach (var item in cases.RootElement.EnumerateArray())
            {
                string name = item.GetProperty("name").GetString()!;
                string resourceRoot = Path.Combine(root, "resources");
                Directory.CreateDirectory(resourceRoot);
                using (var resources = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository(), "reference/fixtures/data.json"))))
                    foreach (var resource in resources.RootElement.EnumerateObject())
                        File.WriteAllText(Path.Combine(resourceRoot, resource.Name + ".json"), resource.Value.GetRawText());
                string dataFile = Path.Combine(root, "data.json");
                File.WriteAllText(dataFile, item.GetProperty("data").GetRawText());
                var reference = Run("node", Repository(), "tools/node-reference/cli.mjs", "--template", name, "--template-root", root, "--data-file", dataFile);
                int expectedExit = item.TryGetProperty("exitCode", out var exitCode) ? exitCode.GetInt32() : 0;
                Assert.True(reference.Exit == expectedExit, reference.Error);
                Match(item.GetProperty("expected").GetString()!, reference.Output, name + " reference");
                var actual = Run("dotnet", root, "output/GeneratedTemplates.dll", "--template", name, "--data-file", dataFile, "--data-root", resourceRoot);
                Assert.True(actual.Exit == expectedExit, name + ": " + actual.Error);
                Match(expectedExit == 0 ? reference.Output : "", actual.Output, name + " " + item.GetProperty("data").GetRawText());
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void GeneratedProgramRunsIndependentlyAndMatchesNode()
    {
        string root = Path.Combine(Path.GetTempPath(), "texttemplate-ab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string input = Path.Combine(root, "input"), isolated = Path.Combine(root, "isolated");
            Directory.CreateDirectory(Path.Combine(input, "templates")); Directory.CreateDirectory(Path.Combine(input, "subtemplates"));
            Directory.CreateDirectory(isolated);
            var cases = new Dictionary<string, string>
            {
                ["greeting"] = "Hello {firstName}!",
                ["a-b"] = "Hyphen",
                ["aB"] = "Collision",
                ["class"] = "Keyword",
                ["StringComparer"] = "Runtime name collision",
                ["TemplateContent"] = "Content type name collision",
                ["a_b"] = "Underscore",
                ["1class"] = "Digit",
                ["café"] = "Unicode",
                ["whitespace"] = "// first comment\nHello\r\n\t{firstName}`\n again\n",
                ["missing"] = "before\nremove {absent} suffix\nafter\n{empty}\n{nil}\nend",
                ["numbers"] = "{zero},{negative},{decimalValue},{small},{large},{yes},{no}",
                ["predicates"] = "{firstName=>[yes],[no]} {no=>[yes],[no]} {zero=>[yes],[no]} {absent=>[yes],[no]}",
                ["arrows"] = "before {absent->[hidden]} after\nremove {absent=>[hidden]} line\nend",
                ["logical"] = "{firstName & !absent} {(zero=0) | no} {absent|no&yes}",
                ["relations"] = "{decimalValue=1} {absent=other} {firstName!=absent} {zero<negative} {firstName>\"A\"}",
                ["quotes"] = "{\"line\\nnext\"} {\"quote\\\" and \\u263a\"} {'it\\'s'}",
                ["context"] = "{person:[{name} / {^.firstName}]} {person.name}",
                ["jsonContext"] = "{'{\"firstName\":\"Local\"}':[Hi {firstName}]}",
                ["fallback"] = "{absent:[not used],[fallback]}",
                ["subtemplates"] = "{#local} {#shared}\nSubtemplates:\n{#local:[Local {firstName}]}",
                ["nestedScope"] = "{#outer}\nSubtemplates:\n{#outer:[{#inner}\nSubtemplates:\n{#inner:[Inside {firstName}]}]}",
                ["inheritedLocals"] = "{person:[{#local} / {^.firstName}]} {firstName}\nSubtemplates:\n{#local:[{name}]}",
                ["scopeIsolation"] = "{#outer} {#local}\nSubtemplates:\n{#local:[Root {firstName}]}\n{#outer:[{person:[{#local}]}\nSubtemplates:\n{#local:[Inner {name}]}]}",
                ["rawQuotes"] = "Quotes \"\"\"\n\t{firstName}\n\nEnd\n",
                ["nameCollisions"] = "{#Evaluate} {#Get} {#class} {firstName} {firstName}\nSubtemplates:\n{#Evaluate:[Evaluate]}\n{#Get:[Get]}\n{#class:[Keyword]}",
                ["builtins"] = "{#Join} {#Builtins} {Builtins} {firstName.Join(',')}\nSubtemplates:\n{#Join:[template join]}\n{#Builtins:[template builtins]}",
                ["helperCollisions"] = "{#IsTruthy} {#MethodInput} {IsTruthy} {MethodInput} {firstName=>[yes],[no]} {firstName.Join(',')}\nSubtemplates:\n{#IsTruthy:[truth]}\n{#MethodInput:[input]}",
                ["registrationCollisions"] = "{#defineSubtemplates} {#ApplySubtemplate} {#SubtemplateInput} {#SubtemplateDefinitions} {ApplySubtemplate} {SubtemplateInput}\nSubtemplates:\n{#defineSubtemplates:[definition]}\n{#ApplySubtemplate:[application]}\n{#SubtemplateInput:[input]}\n{#SubtemplateDefinitions:[scope]}",
                ["birthdayComplete"] = "{'{\"fname\":\"Ada\",\"lname\":\"Lovelace\",\"date\":\"Dec 10\"}':[before\n{fname->[{fname} ]}{lname} {#date}\nafter]}\nSubtemplates:\n{#date:[{date}]}",
                ["birthdayOptionalNull"] = "{'{\"fname\":null,\"lname\":\"Lovelace\",\"date\":\"Dec 10\"}':[before\n{fname->[{fname} ]}{lname} {#date}\nafter]}\nSubtemplates:\n{#date:[{date}]}",
                ["birthdayRequiredNull"] = "{'{\"fname\":\"Ada\",\"lname\":null,\"date\":\"Dec 10\"}':[before\n{fname->[{fname} ]}{lname} {#date}\nafter]}\nSubtemplates:\n{#date:[{date}]}",
                ["birthdayRequiredAbsent"] = "{'{\"fname\":\"Ada\",\"date\":\"Dec 10\"}':[before\n{fname->[{fname} ]}{lname} {#date}\nafter]}\nSubtemplates:\n{#date:[{date}]}",
                ["birthdayNestedNull"] = "{'{\"fname\":\"Ada\",\"lname\":\"Lovelace\",\"date\":null}':[before\n{fname->[{fname} ]}{lname} {#date}\nafter]}\nSubtemplates:\n{#date:[{date}]}",
                ["typedBranches"] = "{yes=>'one','two'} {no=>'missing'} {yes->'text'} {yes=>'text',firstName} {yes=>[fragments]} {zero=0} {yes & !no}",
                ["keywordContext"] = "{person:[{class}]}",
                ["literalDelimiters"] = "{\"{{{{ }}}} \\\"\\\"\\\"\\\"\"}\n{firstName}\n",
                ["emptyInline"] = "a{:[]}b"
            };
            using var includeCases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository(), "tests/node-reference/include-cases.json")));
            foreach (var item in includeCases.RootElement.EnumerateArray())
            {
                cases["include-" + item.GetProperty("name").GetString()!] = item.GetProperty("script").GetString()!;
                foreach (var shared in item.GetProperty("shared").EnumerateObject())
                    File.WriteAllText(Path.Combine(input, "subtemplates", shared.Name + ".template"), shared.Value.GetString());
            }
            foreach (var (name, text) in cases) File.WriteAllText(Path.Combine(input, "templates", name + ".template"), text);
            File.WriteAllText(Path.Combine(input, "subtemplates", "shared.template"), "[Shared {person.name}]");
            string data = """{"firstName":"Ada","person":{"name":"Grace","class":"Example"},"zero":0,"negative":-2,"decimalValue":1.8,"small":1e-7,"large":1e21,"yes":true,"no":false,"empty":"","nil":null}""";
            string dataFile = Path.Combine(isolated, "input.json"); File.WriteAllText(dataFile, data);
            var expected = new Dictionary<string, string>();
            foreach (string name in cases.Keys)
            {
                var reference = Run("node", Repository(), "tools/node-reference/cli.mjs", "--template", name, "--template-root", input, "--data-file", dataFile);
                Assert.True(reference.Exit == 0, name + ": " + reference.Error);
                expected[name] = reference.Output;
            }
            var parsed = new TextTemplateCompiler().CompileDirectory(input);
            Assert.True(parsed.Success, string.Join("\n", parsed.Diagnostics));
            var generated = new CSharpEmitter().Generate(parsed.Document!);
            Assert.True(generated.Success, string.Join("\n", generated.Diagnostics));
            Assert.DoesNotContain("SyntaxNode", generated.Source);
            Assert.DoesNotMatch(@"\bEvaluate\d+\b", generated.Source!);
            Assert.Contains("internal sealed class Greeting", generated.Source);
            Assert.Contains("internal TemplateContent Evaluate()", generated.Source);
            Assert.Contains("Line($\"Hello {firstName}!\")", generated.Source);
            Assert.Contains("private TemplateContent local()", generated.Source);
            string typedBranches = generated.Files["Templates/TypedBranches.cs"];
            Assert.Contains("string yesResult =", typedBranches);
            Assert.Contains("string? noResult =", typedBranches);
            Assert.Contains("string yesResult_2 =", typedBranches);
            Assert.Contains("object? yesResult_3 =", typedBranches);
            Assert.Contains("object? yesResult_4 =", typedBranches);
            Assert.Contains("bool content = EvaluationContext.Compare", typedBranches);
            Assert.Contains("bool content_2 =", typedBranches);
            Assert.DoesNotMatch(@"\b(?:Template_|Subtemplate_|Define_|InlineTemplate_L)\w*", generated.Source!);
            Assert.DoesNotMatch(@"\b\w+_L\d+_C\d+\b", generated.Source!);
            Assert.Contains("object? firstName = Get(\"firstName\");", generated.Source);
            Assert.Contains("private TemplateContent @class()", generated.Source);
            Assert.Contains("private TemplateContent Evaluate_2()", generated.Source);
            Assert.Contains("IsTruthy(\"firstName\")", generated.Source);
            Assert.Contains("MethodInput(\"firstName\")", generated.Source);
            Assert.Contains("EvaluationContext.Invoke(MethodInput(\"firstName\")", generated.Source);
            Assert.Contains("=> Builtins.Join(", generated.Source);
            Assert.Contains("Support/Builtins.cs", generated.Files.Keys);
            Assert.Contains("Templates/Builtins_2.cs", generated.Files.Keys);
            Assert.Contains("private TemplateContent Join()", generated.Source);
            Assert.Contains("private TemplateContent Builtins_3()", generated.Source);
            Assert.DoesNotContain("protected static object? Join(", generated.Source);
            Assert.Contains("private TemplateContent IsTruthy_2()", generated.Source);
            Assert.Contains("private TemplateContent MethodInput_2()", generated.Source);
            Assert.Contains("Templates/AB_2.cs", generated.Files.Keys);
            Assert.Contains("Includes/Common.cs", generated.Files.Keys);
            Assert.Contains("internal sealed class Shared", generated.Source);
            Assert.Equal(generated.Source, new CSharpEmitter().Generate(parsed.Document!).Source);
            Assert.DoesNotContain("TextTemplate.Compiler.Parsing", generated.Source);
            Assert.Equal(generated.Files.Keys, new CSharpEmitter().Generate(parsed.Document!).Files.Keys);
            foreach (var (path, content) in generated.Files)
            {
                string target = Path.Combine(isolated, path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, content);
            }
            File.WriteAllText(Path.Combine(isolated, "Standalone.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>""");
            var build = Run("dotnet", isolated, "build", "Standalone.csproj", "--nologo", "-o", "bin-output");
            Assert.True(build.Exit == 0, build.Output + build.Error);
            // Execution cannot read the templates: only generated code and supplied data remain.
            Directory.Delete(input, true);
            foreach (var name in cases.Keys)
            {
                var actual = Run("dotnet", isolated, "bin-output/Standalone.dll", "--template", name, "--data-file", dataFile);
                Assert.True(actual.Exit == 0, name + ": " + actual.Error);
                Match(expected[name], actual.Output, name);
            }
            var deps = File.ReadAllText(Path.Combine(isolated, "bin-output", "Standalone.deps.json"));
            Assert.DoesNotContain("TextTemplate", deps);
            // Extracted data files are actual input values, not the named fixture database.
            Assert.Contains("Support/TemplateContent.cs", generated.Files.Keys);
            foreach (string dataName in new[] { "nested", "escapeTest", "simpledata" })
            {
                string fixture = Path.Combine(Repository(), "reference", "data", dataName + ".json");
                var reference = Run("node", Repository(), "tools/node-reference/cli.mjs",
                    "--template-text", cases["greeting"], "--data-file", fixture);
                var actual = Run("dotnet", isolated, "bin-output/Standalone.dll",
                    "--template", "greeting", "--data-file", fixture);
                Assert.True(reference.Exit == 0, reference.Error);
                Assert.True(actual.Exit == 0, actual.Error);
                Match(reference.Output, actual.Output, "greeting with " + fixture);
            }
            // Root lists project properties without repeating the whole root template.
            File.WriteAllText(dataFile, "[{\"firstName\":\"A\"},{\"firstName\":\"B\"}]");
            var list = Run("dotnet", isolated, "bin-output/Standalone.dll", "--template", "greeting", "--data-file", dataFile);
            Assert.Equal(0, list.Exit); Assert.Equal("Hello A, B!", list.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EntireReferenceBuildsAndStubsFailOnlyWhenReached()
    {
        string root = Path.Combine(Path.GetTempPath(), "texttemplate-stubs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var parsed = new TextTemplateCompiler().CompileDirectory(Path.Combine(Repository(), "reference"));
            Assert.True(parsed.Success, string.Join("\n", parsed.Diagnostics));
            var generated = new CSharpEmitter().Generate(parsed.Document!);
            Assert.True(generated.Success, string.Join("\n", generated.Diagnostics));
            Assert.Contains("Includes/DoInclude.cs", generated.Files.Keys);
            Assert.Contains("Includes/Common.cs", generated.Files.Keys);
            string simpledata = generated.Files["Templates/Simpledata.cs"];
            Assert.Contains("EvaluationContext.Invoke(SubtemplateInput(\"xformatPet\")", simpledata);
            Assert.Contains("=> ApplySubtemplate(\"anded\",", simpledata);
            Assert.Contains("dataContext => new Simpledata(dataContext)", simpledata);
            Assert.Contains("definitionContext => definitionContext.Each(itemContext =>", simpledata);
            Assert.Contains("annotatedContext => annotatedContext.Include", simpledata);
            Assert.Contains("annotatedContext => annotatedContext.Each(itemContext =>", simpledata);
            Assert.DoesNotContain("child =>", simpledata);
            Assert.Contains("WithDefinitions(defineSubtemplates,", simpledata);
            Assert.Contains("private void defineSubtemplates(SubtemplateDefinitions definitions)", simpledata);
            Assert.Contains("definitions.Add(\"formatPet\",", simpledata);
            Assert.Contains("object? petsResult =", simpledata);
            Assert.DoesNotContain("new Dictionary", simpledata);
            Assert.DoesNotContain("CallNamed(", simpledata);
            string wikipedia = generated.Files["Templates/Wikipedia.cs"];
            Assert.Contains("private TemplateContent getWiki()", wikipedia);
            Assert.DoesNotContain("EvaluationContext.At(", wikipedia);
            Assert.Contains("Builtins.Replace(", wikipedia);
            Assert.Contains("new TemplateRegex(", wikipedia);
            Assert.Contains("object? extract = Get(\"key.extract\");", wikipedia);
            File.WriteAllText(Path.Combine(root, "Generated.cs"), generated.Source);
            File.WriteAllText(Path.Combine(root, "Standalone.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><StartupObject>Probe</StartupObject></PropertyGroup></Project>""");
            // Compile every legacy template and subtemplate, including unfinished features.
            File.WriteAllText(Path.Combine(root, "Probe.cs"), """
                using System;
                public static class Probe {
                    public static void Main(string[] args) {
                        if (args.Length > 0 && args[0] == "--regex-probe") {
                            using var inputs = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(args[1]));
                            var results = new System.Collections.Generic.List<object>();
                            foreach (var item in inputs.RootElement.EnumerateArray()) {
                                var pattern = new TemplateRegex("/" + item.GetProperty("pattern").GetString() + "/" + item.GetProperty("flags").GetString(), "probe");
                                string text = item.GetProperty("text").GetString();
                                bool first = pattern.Test(text), second = pattern.Test(text);
                                string replacement = pattern.Replace(text, item.GetProperty("replacement").GetString());
                                bool third = pattern.Test(text);
                                results.Add(new { first, second, replacement, third });
                            }
                            Console.Write(System.Text.Json.JsonSerializer.Serialize(results));
                            return;
                        }
                        if (args.Length > 0 && args[0] == "--regex-errors") {
                            foreach (string literal in new[] { "/(/", "/a/gg", "/(?i)a/" }) {
                                try { _ = new TemplateRegex(literal, "sample.template:2:3"); throw new Exception("Invalid regex accepted."); }
                                catch (InvalidOperationException e) { if (!e.Message.Contains("sample.template:2:3")) throw; }
                            }
                            try { _ = new TemplateRegex("/(a(b))+/", "sample.template:2:3"); throw new Exception("Unsupported regex accepted."); }
                            catch (NotImplementedException e) { if (!e.Message.Contains("sample.template:2:3")) throw; }
                            foreach (var method in new Func<object, object[], object>[] { Builtins.Contains, Builtins.StartsWith, Builtins.EndsWith }) {
                                try { method("abc", new object[] { new TemplateRegex("/a/", "probe") }); throw new Exception("Regex argument accepted."); }
                                catch (InvalidOperationException) { }
                            }
                            Console.Write("regex-errors-ok"); return;
                        }
                        if (args.Length > 0 && args[0] == "--casing-probe") {
                            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                            for (int code = 0; code <= 0x10ffff; code++) {
                                if (code >= 0xd800 && code <= 0xdfff) continue;
                                string text = char.ConvertFromUtf32(code);
                                string result = Builtins.ToUpper(text) + "|" + Builtins.ToLower(text) + "\n";
                                hash.AppendData(System.Text.Encoding.UTF8.GetBytes(result));
                            }
                            Console.Write(Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
                            return;
                        }
                        if (args.Length > 0 && args[0] == "--content-probe") {
                            TemplateContent inner = EvaluationContext.Text($"value {true}");
                            TemplateContent outer = EvaluationContext.Text($"before {inner} after");
                            if (outer.Render() != "before value true after" || outer.ToString() != outer.Render())
                                throw new Exception("Content rendering failed.");
                            TemplateContent missing = EvaluationContext.Text($"{new EvaluationContext.Missing("absent")}");
                            if (missing.Render() != "" || EvaluationContext.Compose(missing) is not null)
                                throw new Exception("Missing content lost its internal distinction from empty text.");
                            TemplateContent nested = EvaluationContext.Text($"remove {missing} suffix\nkeep");
                            if (nested.Render() != "keep") throw new Exception("Nested content was flattened early.");
                            Console.Write("content-ok");
                            return;
                        }
                        if (args.Length > 0) { Environment.ExitCode = GeneratedTemplateProgram.Main(args); return; }
                        try { GeneratedTemplates.Render("startup"); }
                        catch (NotImplementedException e) { Console.Write(e.Message); }
                    }
                }
                """);
            var build = Run("dotnet", root, "build", "--nologo", "-o", "output");
            Assert.True(build.Exit == 0, build.Output + build.Error);
            var contentProbe = Run("dotnet", root, "output/Standalone.dll", "--content-probe");
            Assert.True(contentProbe.Exit == 0, contentProbe.Output + contentProbe.Error);
            Assert.Equal("content-ok", contentProbe.Output);
            var casingProbe = Run("dotnet", root, "output/Standalone.dll", "--casing-probe");
            Assert.True(casingProbe.Exit == 0, casingProbe.Error);
            var nodeCasing = Run("node", Repository(), "-e", """
                const hash = require('node:crypto').createHash('sha256');
                for (let code = 0; code <= 0x10ffff; code++) {
                    if (code >= 0xd800 && code <= 0xdfff) continue;
                    const text = String.fromCodePoint(code);
                    hash.update(text.toUpperCase() + '|' + text.toLowerCase() + '\n');
                }
                process.stdout.write(hash.digest('hex'));
                """);
            Assert.True(nodeCasing.Exit == 0, nodeCasing.Error);
            Assert.Equal(nodeCasing.Output, casingProbe.Output);
            string regexInputs = Path.Combine(Repository(), "tests/node-reference/regex-engine-cases.json");
            var regexProbe = Run("dotnet", root, "output/Standalone.dll", "--regex-probe", regexInputs);
            Assert.True(regexProbe.Exit == 0, regexProbe.Error);
            var nodeRegex = Run("node", Repository(), "-e", """
                const inputs = JSON.parse(require('node:fs').readFileSync(process.argv[1], 'utf8'));
                process.stdout.write(JSON.stringify(inputs.map(item => {
                    const pattern = new RegExp(item.pattern, item.flags);
                    const first = pattern.test(item.text), second = pattern.test(item.text);
                    const replacement = item.text.replace(pattern, item.replacement);
                    const third = pattern.test(item.text);
                    return {first, second, replacement, third};
                })));
                """, regexInputs);
            Assert.True(nodeRegex.Exit == 0, nodeRegex.Error);
            using var regexActual = JsonDocument.Parse(regexProbe.Output);
            using var regexExpected = JsonDocument.Parse(nodeRegex.Output);
            for (int i = 0; i < regexExpected.RootElement.GetArrayLength(); i++)
                foreach (string field in new[] { "first", "second", "replacement", "third" })
                    Match(regexExpected.RootElement[i].GetProperty(field).ToString(), regexActual.RootElement[i].GetProperty(field).ToString(), "regex engine " + i + " " + field);
            var regexErrors = Run("dotnet", root, "output/Standalone.dll", "--regex-errors");
            Assert.True(regexErrors.Exit == 0, regexErrors.Error);
            Assert.Equal("regex-errors-ok", regexErrors.Output);
            Assert.Contains("Support/TemplateContent.cs", generated.Files.Keys);
            foreach (string dataName in new[] { "nested", "escapeTest", "simpledata" })
            {
                string dataFile = Path.Combine(Repository(), "reference/data", dataName + ".json");
                var reference = Run("node", Repository(), "tools/node-reference/cli.mjs", "--template", "simpledata", "--template-root", "reference", "--data-file", dataFile);
                var actual = Run("dotnet", root, "output/Standalone.dll", "--template", "simpledata", "--data-file", dataFile);
                Assert.True(reference.Exit == 0, reference.Error);
                Assert.True(actual.Exit == 0, actual.Error);
                Match(reference.Output, actual.Output, "simpledata with " + dataName);
            }
            string input = Path.Combine(root, "input");
            Directory.CreateDirectory(Path.Combine(input, "templates"));
            Directory.CreateDirectory(Path.Combine(input, "subtemplates"));
            File.WriteAllText(Path.Combine(input, "templates", "safe.template"), "{absent=>[{value.ToDate()}],[safe]}");
            File.WriteAllText(Path.Combine(input, "templates", "stub.template"), "{value.ToDate()}");
            generated = new CSharpEmitter().Generate(new TextTemplateCompiler().CompileDirectory(input).Document!);
            File.WriteAllText(Path.Combine(root, "Generated.cs"), generated.Source);
            File.WriteAllText(Path.Combine(root, "Probe.cs"), """
                using System;
                public static class Probe {
                    public static void Main() {
                        Console.WriteLine(GeneratedTemplates.Render("safe"));
                        try { GeneratedTemplates.Render("stub"); }
                        catch (NotImplementedException e) { Console.Write(e.Message); }
                    }
                }
                """);
            build = Run("dotnet", root, "build", "--nologo", "-o", "output");
            Assert.True(build.Exit == 0, build.Output + build.Error);
            var result = Run("dotnet", root, "output/Standalone.dll");
            Assert.Equal(0, result.Exit);
            Assert.StartsWith("safe\n", result.Output);
            Assert.Contains("templates/stub.template:1:", result.Output);
            Assert.Contains("Call ToDate", result.Output);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DirectoryProjectIsEditableAndSyntaxFailurePreservesFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "texttemplate-directory-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "templates"));
            Directory.CreateDirectory(Path.Combine(root, "subtemplates"));
            File.WriteAllText(Path.Combine(root, "templates", "hello.template"), "Hello {firstName}!");
            File.WriteAllText(Path.Combine(root, "templates", "other.template"), "Other");
            string output = Path.Combine(root, "Generated");
            Assert.Equal(0, Program.Main(["--root", root, "--output", output]));
            string unit = Path.Combine(output, "Templates", "Hello.cs");
            File.WriteAllText(unit, File.ReadAllText(unit).Replace("Hello ", "Welcome "));
            var build = Run("dotnet", output, "build", "--nologo");
            Assert.True(build.Exit == 0, build.Output + build.Error);
            string data = Path.Combine(root, "input.json");
            File.WriteAllText(data, """{"firstName":"Ada"}""");
            var result = Run("dotnet", output, "bin/Debug/net10.0/GeneratedTemplates.dll", "--template", "hello", "--data-file", data);
            Assert.Equal(0, result.Exit);
            Assert.Equal("Welcome Ada!", result.Output);
            var original = Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
            File.WriteAllText(Path.Combine(root, "templates", "bad.template"), "{");
            Assert.Equal(1, Program.Main(["--root", root, "--output", output]));
            foreach (var (path, content) in original) Assert.Equal(content, File.ReadAllText(path));
            File.Delete(Path.Combine(root, "templates", "bad.template"));
            Assert.Equal(0, Program.Main(["--root", root, "--template", "hello", "--output", output]));
            Assert.DoesNotContain("Other.cs", File.ReadAllText(Path.Combine(output, "GeneratedTemplates.csproj")));
            Assert.True(File.Exists(Path.Combine(output, "Templates", "Other.cs")));
            build = Run("dotnet", output, "build", "--nologo");
            Assert.True(build.Exit == 0, build.Output + build.Error);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DefaultIsCSharpJsonIsOptionalAndSyntaxFailureIsNotPublished()
    {
        string root = Path.Combine(Path.GetTempPath(), "texttemplate-emitter-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "templates")); Directory.CreateDirectory(Path.Combine(root, "subtemplates"));
            File.WriteAllText(Path.Combine(root, "templates", "hello.template"), "Hello {firstName}");
            string output = Path.Combine(root, "Generated.cs");
            Assert.Equal(0, Program.Main(["--root", root, "--output", output]));
            Assert.Contains("public static class GeneratedTemplates", File.ReadAllText(output));
            string json = Path.Combine(root, "trees.json");
            Assert.Equal(0, Program.Main(["--root", root, "--format", "json", "--output", json]));
            Assert.True(JsonDocument.Parse(File.ReadAllText(json)).RootElement.GetProperty("sources").TryGetProperty("templates/hello.template", out _));
            byte[] original = File.ReadAllBytes(output);
            File.WriteAllText(Path.Combine(root, "templates", "unsupported.template"), "{firstName.ToDate()}");
            Assert.Equal(0, Program.Main(["--root", root, "--output", output]));
            Assert.Contains("Call ToDate", File.ReadAllText(output));
            Assert.Equal(0, Program.Main(["--root", root, "--template", "hello", "--output", output]));
            Assert.Equal(original, File.ReadAllBytes(output));
            File.WriteAllText(Path.Combine(root, "templates", "unsupported.template"), "{");
            Assert.Equal(1, Program.Main(["--root", root, "--template", "hello", "--output", output]));
            Assert.Equal(original, File.ReadAllBytes(output));
        }
        finally { Directory.Delete(root, true); }
    }
}
