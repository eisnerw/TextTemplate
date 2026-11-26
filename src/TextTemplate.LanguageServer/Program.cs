using System.Text;
using System.Text.Json;
using TextTemplate.Compiler.Parsing;

internal static class Program
{
    private static readonly SemaphoreSlim OutputLock = new(1, 1);
    private static readonly Dictionary<string, string> OpenDocuments = new(StringComparer.Ordinal);
    private static string? workspaceRoot;

    private static async Task Main()
    {
        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        while (true)
        {
            JsonDocument? message;
            try
            {
                message = await ReadMessageAsync(input);
            }
            catch (Exception error) when (error is JsonException or InvalidDataException or EndOfStreamException)
            {
                Console.Error.WriteLine(error.Message);
                return;
            }

            if (message is null) return;
            using (message)
            {
                try
                {
                    if (!await HandleMessageAsync(message.RootElement, output)) return;
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine(error);
                }
            }
        }
    }

    private static async Task<bool> HandleMessageAsync(JsonElement message, Stream output)
    {
        if (!message.TryGetProperty("method", out var methodElement)) return true;
        string? method = methodElement.GetString();
        bool hasId = message.TryGetProperty("id", out var requestId);
        JsonElement id = hasId ? requestId.Clone() : default;
        JsonElement parameters = message.TryGetProperty("params", out var value) ? value : default;

        switch (method)
        {
            case "initialize":
                SetWorkspaceRoot(parameters);
                await SendAsync(output, new
                {
                    jsonrpc = "2.0",
                    id,
                    result = new
                    {
                        capabilities = new
                        {
                            textDocumentSync = new { openClose = true, change = 1 },
                            definitionProvider = true,
                            foldingRangeProvider = true
                        }
                    }
                });
                break;
            case "shutdown":
                await SendAsync(output, new { jsonrpc = "2.0", id, result = (object?)null });
                break;
            case "exit":
                return false;
            case "textDocument/didOpen":
                await OpenDocumentAsync(parameters, output);
                break;
            case "textDocument/didChange":
                await ChangeDocumentAsync(parameters, output);
                break;
            case "textDocument/didClose":
                await CloseDocumentAsync(parameters, output);
                break;
            case "textDocument/definition":
                await DefinitionAsync(id, parameters, output);
                break;
            case "textDocument/foldingRange":
                await FoldingRangeAsync(id, parameters, output);
                break;
            default:
                if (hasId)
                {
                    await SendAsync(output, new
                    {
                        jsonrpc = "2.0",
                        id,
                        error = new { code = -32601, message = $"Method not found: {method}" }
                    });
                }
                break;
        }

        return true;
    }

    private static void SetWorkspaceRoot(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object) return;
        if (parameters.TryGetProperty("rootUri", out var rootUri))
            workspaceRoot = FilePath(rootUri.GetString());
        if (workspaceRoot is not null) return;
        if (parameters.TryGetProperty("rootPath", out var rootPath) && rootPath.ValueKind == JsonValueKind.String)
            workspaceRoot = rootPath.GetString();
        if (workspaceRoot is not null) return;
        if (parameters.TryGetProperty("workspaceFolders", out var folders)
            && folders.ValueKind == JsonValueKind.Array
            && folders.GetArrayLength() > 0)
        {
            workspaceRoot = FilePath(folders[0].GetProperty("uri").GetString());
        }
    }

    private static async Task OpenDocumentAsync(JsonElement parameters, Stream output)
    {
        JsonElement document = parameters.GetProperty("textDocument");
        string uri = document.GetProperty("uri").GetString()!;
        string text = document.GetProperty("text").GetString() ?? "";
        OpenDocuments[uri] = text;
        await PublishDiagnosticsAsync(uri, text, output);
    }

    private static async Task ChangeDocumentAsync(JsonElement parameters, Stream output)
    {
        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        JsonElement changes = parameters.GetProperty("contentChanges");
        if (changes.GetArrayLength() == 0) return;
        string text = changes[changes.GetArrayLength() - 1].GetProperty("text").GetString() ?? "";
        OpenDocuments[uri] = text;
        await PublishDiagnosticsAsync(uri, text, output);
    }

    private static async Task CloseDocumentAsync(JsonElement parameters, Stream output)
    {
        string uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString()!;
        OpenDocuments.Remove(uri);
        await SendAsync(output, new
        {
            jsonrpc = "2.0",
            method = "textDocument/publishDiagnostics",
            @params = new { uri, diagnostics = Array.Empty<object>() }
        });
    }

    private static async Task DefinitionAsync(JsonElement id, JsonElement parameters, Stream output)
    {
        JsonElement document = parameters.GetProperty("textDocument");
        string uri = document.GetProperty("uri").GetString()!;
        string? text = OpenDocuments.GetValueOrDefault(uri);
        string? path = FilePath(uri);
        if (text is null && path is not null && File.Exists(path)) text = await File.ReadAllTextAsync(path);
        object[] locations = [];
        if (workspaceRoot is not null && text is not null)
        {
            JsonElement position = parameters.GetProperty("position");
            locations = new WorkspaceIndex(workspaceRoot, OpenDocuments).FindDefinitions(
                uri, text, position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
        }
        await SendAsync(output, new { jsonrpc = "2.0", id, result = locations });
    }

    private static async Task FoldingRangeAsync(JsonElement id, JsonElement parameters, Stream output)
    {
        JsonElement document = parameters.GetProperty("textDocument");
        string uri = document.GetProperty("uri").GetString()!;
        string? text = OpenDocuments.GetValueOrDefault(uri);
        string? path = FilePath(uri);
        if (text is null && path is not null && File.Exists(path)) text = await File.ReadAllTextAsync(path);

        object[] ranges = [];
        if (text is not null)
        {
            bool sharedTemplate = false;
            if (path is not null && workspaceRoot is not null
                && WorkspaceIndex.TryFindTemplateRoot(workspaceRoot, path, out _, out string category))
            {
                sharedTemplate = category == "subtemplates";
            }

            string sourceName = path is null ? uri : Path.GetRelativePath(workspaceRoot ?? ".", path).Replace('\\', '/');
            var source = new SourceText(sourceName, text);
            var parser = new TemplateParser(source, sharedTemplate);
            try
            {
                var tree = parser.Parse(sharedTemplate);
                ranges = FoldingRanges(source, tree);
            }
            catch (SyntaxException)
            {
                ranges = [];
            }
        }

        await SendAsync(output, new { jsonrpc = "2.0", id, result = ranges });
    }

    private static object[] FoldingRanges(SourceText source, SyntaxNode tree)
    {
        var ranges = new List<object>();

        void Visit(SyntaxNode node)
        {
            if (node.Kind is SyntaxKind.SubtemplateSection or SyntaxKind.InlineTemplate)
            {
                AddRange(node.Span);
            }

            if (node.Kind == SyntaxKind.SubtemplateSection)
            {
                foreach (SyntaxNode child in node.Children)
                    if (child.Kind == SyntaxKind.Context && child.Value == "explicit")
                        AddRange(child.Span);
            }

            foreach (SyntaxNode child in node.Children) Visit(child);
        }

        void AddRange(SourceSpan span)
        {
            var start = PositionAtSource(source.Original, span.Offset);
            var end = PositionAtSource(source.Original, span.Offset + span.Length);
            int startLine = ((Position)start).Line;
            int endLine = ((Position)end).Line;
            if (endLine > startLine)
            {
                ranges.Add(new
                {
                    startLine,
                    endLine,
                    kind = "region"
                });
            }
        }

        Visit(tree);
        return ranges.DistinctBy(range => JsonSerializer.Serialize(range)).ToArray();
    }

    private readonly record struct Position(int Line, int Character);

    private static Position PositionAtSource(string text, int offset)
    {
        int line = 0;
        int character = 0;
        int limit = Math.Clamp(offset, 0, text.Length);
        for (int index = 0; index < limit; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < limit && text[index + 1] == '\n') index++;
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
        return new Position(line, character);
    }

    private static async Task PublishDiagnosticsAsync(string uri, string text, Stream output)
    {
        var diagnostics = new List<object>();
        string sourceName = uri;
        bool sharedTemplate = false;
        string? filePath = FilePath(uri);

        if (filePath is not null && workspaceRoot is not null
            && WorkspaceIndex.TryFindTemplateRoot(workspaceRoot, filePath, out string templateRoot, out string category))
        {
            sourceName = Path.GetRelativePath(templateRoot, filePath).Replace('\\', '/');
            sharedTemplate = category == "subtemplates";
        }

        try
        {
            var source = new SourceText(sourceName, text);
            var parser = new TemplateParser(source, sharedTemplate);
            parser.Parse(sharedTemplate);
        }
        catch (SyntaxException error)
        {
            diagnostics.Add(ToLspDiagnostic(error.Diagnostic.Message, error.Diagnostic.Span, text));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            diagnostics.Add(new
            {
                range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
                severity = 1,
                source = "TextTemplate",
                message = "Parser failure: " + error.Message
            });
        }

        await SendAsync(output, new
        {
            jsonrpc = "2.0",
            method = "textDocument/publishDiagnostics",
            @params = new { uri, diagnostics }
        });
    }

    private static object ToLspDiagnostic(string message, SourceSpan span, string text)
    {
        var start = PositionAt(text, span.Offset);
        var end = PositionAt(text, span.Offset + span.Length);
        return new
        {
            range = new { start, end },
            severity = 1,
            source = "TextTemplate",
            message
        };
    }

    private static object PositionAt(string text, int offset)
    {
        int line = 0;
        int character = 0;
        int limit = Math.Clamp(offset, 0, text.Length);
        for (int index = 0; index < limit; index++)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < limit && text[index + 1] == '\n') index++;
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

    private static string? FilePath(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;

    private static async Task<JsonDocument?> ReadMessageAsync(Stream input)
    {
        var header = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            int count = await input.ReadAsync(one);
            if (count == 0) return header.Count == 0 ? null : throw new EndOfStreamException("Incomplete LSP header.");
            header.Add(one[0]);
            int size = header.Count;
            if (size >= 4 && header[size - 4] == '\r' && header[size - 3] == '\n' && header[size - 2] == '\r' && header[size - 1] == '\n') break;
            if (size > 16_384) throw new InvalidDataException("LSP header is too large.");
        }

        string headerText = Encoding.ASCII.GetString(header.ToArray(), 0, header.Count - 4);
        int? contentLength = null;
        foreach (string line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[15..].Trim(), out int parsedLength))
            {
                contentLength = parsedLength;
            }
        }
        if (contentLength is null or < 0 or > 16_777_216) throw new InvalidDataException("Missing or invalid LSP Content-Length.");

        byte[] body = new byte[contentLength.Value];
        int offset = 0;
        while (offset < body.Length)
        {
            int count = await input.ReadAsync(body.AsMemory(offset));
            if (count == 0) throw new EndOfStreamException("Incomplete LSP message body.");
            offset += count;
        }
        return JsonDocument.Parse(body);
    }

    private static async Task SendAsync(Stream output, object message)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await OutputLock.WaitAsync();
        try
        {
            await output.WriteAsync(header);
            await output.WriteAsync(body);
            await output.FlushAsync();
        }
        finally
        {
            OutputLock.Release();
        }
    }
}