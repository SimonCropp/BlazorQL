/// <summary>
/// Where every line of one text starts, computed once. Converting an offset to a line and column
/// (or back) is otherwise a scan from the beginning of the document, and both directions are asked
/// for once per diagnostic marker and once per automatically inserted leaf — over text that a
/// keystroke has just changed.
/// </summary>
sealed class LineIndex
{
    readonly string text;
    readonly int[] starts;

    public LineIndex(string text)
    {
        this.text = text;

        var count = 1;
        foreach (var character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        starts = new int[count];
        var line = 1;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                starts[line++] = index + 1;
            }
        }
    }

    /// <summary>
    /// The offset of a one-based line and column. A line past the end of the text answers with the
    /// end of it, which is what a scan that ran out of document produced.
    /// </summary>
    public int Offset(int line, int column)
    {
        if (line > starts.Length)
        {
            return text.Length;
        }

        var start = starts[Math.Max(line, 1) - 1];
        return Math.Min(start + Math.Max(column - 1, 0), text.Length);
    }

    /// <summary>The one-based line and column an offset falls on.</summary>
    public (int Line, int Column) LineColumn(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = Array.BinarySearch(starts, offset);
        if (line < 0)
        {
            // Not a line start: the line it belongs to is the one before the insertion point.
            line = ~line - 1;
        }

        return (line + 1, offset - starts[line] + 1);
    }

    /// <summary>The span between two offsets, as monaco wants it.</summary>
    public BlazorMonaco.Range Range(int start, int end)
    {
        var (startLine, startColumn) = LineColumn(start);
        var (endLine, endColumn) = LineColumn(end);
        return new()
        {
            StartLineNumber = startLine,
            StartColumn = startColumn,
            EndLineNumber = endLine,
            EndColumn = endColumn
        };
    }
}
