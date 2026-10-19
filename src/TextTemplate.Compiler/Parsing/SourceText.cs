using System.Text;

namespace TextTemplate.Compiler.Parsing;

public sealed class SourceText
{
    public string Name { get; }
    public string Original { get; }
    public string Normalized { get; }
    private readonly int[] starts;
    private readonly int[] ends;
    private readonly int[] lines;

    public SourceText(string name, string original)
    {
        Name = name;
        Original = original;
        var text = new StringBuilder();
        var start = new List<int>();
        var end = new List<int>();
        var line = new List<int> { 0 };
        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] == '\n') line.Add(i + 1);
            if (original[i] == '\r') continue; // interpret() normalization
            int count = original[i] == '\t' ? 4 : 1;
            for (int j = 0; j < count; j++)
            {
                text.Append(original[i] == '\t' ? ' ' : original[i]);
                start.Add(i);
                end.Add(i + 1);
            }
        }
        Normalized = text.ToString(); starts = start.ToArray(); ends = end.ToArray(); lines = line.ToArray();
    }

    public SourceSpan Span(int offset, int length)
    {
        int begin = offset < starts.Length ? starts[offset] : Original.Length;
        int finish = length == 0 ? begin : ends[offset + length - 1];
        return OriginalSpan(begin, finish - begin);
    }

    public SourceSpan OriginalSpan(int offset, int length)
    {
        int line = Array.BinarySearch(lines, offset);
        if (line < 0) line = ~line - 1;
        return new(offset, length, line + 1, offset - lines[line] + 1);
    }

    public SyntaxException Error(string message, SourceSpan span)
    {
        int begin = lines[span.Line - 1];
        int end = Original.IndexOf('\n', begin);
        if (end < 0) end = Original.Length;
        return new(new(Name, message, span, Original[begin..end].TrimEnd('\r')));
    }
}
