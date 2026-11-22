/// <summary>
/// Where each line of a GraphQL text starts, so that an offset becomes a line and column by binary
/// search instead of by scanning the text from its beginning. Line terminators are GraphQL's, as
/// GraphQLParser's <c>Location.FromLinearPosition</c> counts them: <c>\r\n</c>, a lone <c>\r</c>,
/// and <c>\n</c>.
/// </summary>
/// <remarks>
/// <see cref="LineIndex"/> does this for the editors, whose text is always LF. This one is for the
/// validator, which is public and takes whatever text it is given, and has to put each diagnostic
/// exactly where the parser would.
/// </remarks>
sealed class TextPositions
{
    readonly int[] starts;

    public TextPositions(string text)
    {
        List<int> found = [0];
        for (var index = 0; index < text.Length; index++)
        {
            switch (text[index])
            {
                case '\r':
                    if (index + 1 < text.Length &&
                        text[index + 1] == '\n')
                    {
                        index++;
                    }

                    found.Add(index + 1);
                    break;
                case '\n':
                    found.Add(index + 1);
                    break;
            }
        }

        // The parser does not count a terminator that ends the text: the end of "a\r" is still on
        // line 1. No node starts there, but matching it exactly is what lets this be checked
        // against the parser at every offset.
        if (found.Count > 1 &&
            found[^1] == text.Length)
        {
            found.RemoveAt(found.Count - 1);
        }

        starts = [.. found];
    }

    /// <summary>The one-based line and column of an offset.</summary>
    public (int Line, int Column) LineColumn(int offset)
    {
        var line = Array.BinarySearch(starts, offset);
        if (line < 0)
        {
            // Not a line start: the line it belongs to is the one before the insertion point.
            line = ~line - 1;
        }

        return (line + 1, offset - starts[line] + 1);
    }
}
