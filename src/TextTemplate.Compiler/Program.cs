using System.Text.Json;
using TextTemplate.Compiler.Generation;

namespace TextTemplate.Compiler;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                Console.WriteLine("Usage: TextTemplate.Compiler --root DIRECTORY [--output DIRECTORY|FILE] [--format csharp|json] [--template NAME]\nDefault: emit an editable standalone project under Generated/. --format json exports all parse trees instead.\n--template selects one C# entry point and its shared dependencies. All files are syntax-checked.\nC# output uses a directory, or a single file when the output ends in .cs. JSON defaults to stdout. Errors produce no output.");
                return 0;
            }
            string? root = null, output = null, format = null, template = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--root" && root is null && i + 1 < args.Length) root = args[++i];
                else if (args[i] == "--output" && output is null && i + 1 < args.Length) output = args[++i];
                else if (args[i] == "--format" && format is null && i + 1 < args.Length) format = args[++i];
                else if (args[i] == "--template" && template is null && i + 1 < args.Length) template = args[++i];
                else throw new ArgumentException("Unknown, duplicate, or incomplete argument: " + args[i]);
            }
            if (root is null) throw new ArgumentException("Specify --root DIRECTORY. Use --help for usage.");
            format ??= "csharp";
            if (format is not ("csharp" or "json")) throw new ArgumentException("--format must be csharp or json.");
            if (format == "json" && template is not null) throw new ArgumentException("--template applies to C# generation; JSON always contains the complete root.");
            var result = new TextTemplateCompiler().CompileDirectory(root);
            if (!result.Success)
            {
                foreach (var error in result.Diagnostics)
                {
                    Console.Error.WriteLine($"{error.Source}:{error.Span.Line}:{error.Span.Column}: {error.Message}");
                    Console.Error.WriteLine(error.Excerpt);
                    Console.Error.WriteLine(new string(' ', error.Span.Column - 1) + "^");
                }
                return 1;
            }
            string content;
            if (format == "json") content = JsonSerializer.Serialize(result.Document, TextTemplateCompiler.JsonOptions) + "\n";
            else
            {
                var generated = new CSharpEmitter().Generate(result.Document!, template);
                if (!generated.Success)
                {
                    foreach (var error in generated.Diagnostics)
                        Console.Error.WriteLine($"{error.Source}:{error.Span.Line}:{error.Span.Column}: {error.Message}");
                    return 1;
                }
                if (output is null || !output.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    WriteProject(generated, output ?? "Generated");
                    return 0;
                }
                content = generated.Source!;
            }
            if (output is null) Console.Write(content);
            else TextTemplateCompiler.WriteText(content, output);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }
    private static void WriteProject(GenerationResult generated, string output)
    {
        // Explicit compile items exclude stale units after a narrower regeneration.
        // Keep those files (and user additions) on disk rather than deleting editable code.
        foreach (var (path, content) in generated.Files)
            TextTemplateCompiler.WriteText(content, Path.Combine(output, path));
        string items = string.Join("\n", generated.Files.Keys.Select(path =>
            "    <Compile Include=\"" + System.Security.SecurityElement.Escape(path) + "\" />"));
        TextTemplateCompiler.WriteText("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
              </PropertyGroup>
              <ItemGroup>
            """ + "\n" + items + "\n  </ItemGroup>\n</Project>\n", Path.Combine(output, "GeneratedTemplates.csproj"));
    }

}
