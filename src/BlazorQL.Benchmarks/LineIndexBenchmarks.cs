/// <summary>
/// Turning offsets into monaco's line and column numbers, over a document with a lot of lines.
/// Diagnostics do it once per marker and the leaf filler once per insertion, on every keystroke
/// that changes what either produces.
/// </summary>
[MemoryDiagnoser]
public class LineIndexBenchmarks
{
    string document = null!;
    int[] offsets = null!;

    /// <summary>Lines in the document, and the number of positions converted over it.</summary>
    [Params(500)]
    public int Lines { get; set; }

    [Params(50)]
    public int Positions { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var builder = new StringBuilder("query Q {\n");
        for (var index = 0; index < Lines; index++)
        {
            builder.Append($"  field{index}\n");
        }

        builder.Append('}');
        document = builder.ToString();

        // Spread across the document, as a document's worth of diagnostics would be.
        offsets = new int[Positions];
        for (var index = 0; index < Positions; index++)
        {
            offsets[index] = document.Length * index / Positions;
        }
    }

    /// <summary>One index built for the pass, as the diagnostics loop does.</summary>
    [Benchmark]
    public int WithAnIndex()
    {
        var lines = new LineIndex(document);
        var total = 0;
        foreach (var offset in offsets)
        {
            var (line, column) = lines.LineColumn(offset);
            total += line + column + lines.Offset(line, column);
        }

        return total;
    }

    /// <summary>A scan from the top of the document per position, as it was before.</summary>
    [Benchmark]
    public int WithAScanPerPosition()
    {
        var total = 0;
        foreach (var offset in offsets)
        {
            var (line, column) = Scan(document, offset);
            total += line + column + ScanOffset(document, line, column);
        }

        return total;
    }

    static (int Line, int Column) Scan(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset && index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    static int ScanOffset(string text, int line, int column)
    {
        var offset = 0;
        var current = 1;
        while (current < line && offset < text.Length)
        {
            if (text[offset] == '\n')
            {
                current++;
            }

            offset++;
        }

        return Math.Min(offset + (column - 1), text.Length);
    }
}
