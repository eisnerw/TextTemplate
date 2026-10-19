using System.Text.Json;

namespace TextTemplate.Compiler;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                Console.WriteLine("Usage: TextTemplate.Compiler --root DIRECTORY [--output FILE]\nParses templates/**/*.template and subtemplates/**/*.template into one JSON map.\nWithout --output, JSON is written to stdout. Syntax errors produce no JSON.");
                return 0;
            }
            string? root = null, output = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--root" && root is null && i + 1 < args.Length) root = args[++i];
                else if (args[i] == "--output" && output is null && i + 1 < args.Length) output = args[++i];
                else throw new ArgumentException("Unknown, duplicate, or incomplete argument: " + args[i]);
            }
            if (root is null) throw new ArgumentException("Specify --root DIRECTORY. Use --help for usage.");
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
            if (output is null) Console.WriteLine(JsonSerializer.Serialize(result.Document, TextTemplateCompiler.JsonOptions));
            else TextTemplateCompiler.WriteJson(result.Document!, output);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }
}
