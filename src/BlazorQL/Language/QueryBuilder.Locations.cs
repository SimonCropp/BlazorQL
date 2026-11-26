namespace BlazorQL;

public static partial class QueryBuilder
{
    /// <summary>
    /// The document an edit splices into, with every node's end where its text ends.
    /// </summary>
    /// <remarks>
    /// The parser's end for a node runs on through a comment that follows it on the line, and the line
    /// break after: in <c>users(first: 1 # page size</c> the value <c>1</c> ends after the newline.
    /// Every edit here splices at node ends — replacing a value, closing a set, cutting a line — so a
    /// splice trusting that end deletes the comment and merges the line into the next. The ends are
    /// put right once, here, rather than at each of the places that read one.
    /// </remarks>
    static GraphQLDocument? ParseDocument(string text)
    {
        if (DocumentInfo.Parse(text).Document is not { } document)
        {
            return null;
        }

        // Nothing in the visitor awaits, so the walk is complete when the call returns.
        new EndTrimmer(text).VisitAsync(document, new()).AsTask().GetAwaiter().GetResult();
        return document;
    }

    /// <summary>
    /// Where the text of a node spanning <paramref name="start"/> to <paramref name="end"/> really
    /// ends: after its last character that is not a comment, whitespace or a comma — GraphQL's
    /// insignificant tokens. Strings are stepped over whole, so a <c>#</c> inside one is text.
    /// </summary>
    internal static int SignificantEnd(string text, int start, int end)
    {
        // A document of comments alone parses with a span that is not inside the text; there is
        // nothing to splice beside, so nothing to put right.
        if (start < 0 ||
            end <= start ||
            end > text.Length ||
            text.AsSpan(start, end - start).IndexOf('#') < 0)
        {
            return end;
        }

        var last = start;
        var index = start;
        while (index < end)
        {
            var ch = text[index];
            switch (ch)
            {
                case '#':
                    while (index < end &&
                           text[index] is not ('\r' or '\n'))
                    {
                        index++;
                    }

                    continue;

                case ' ' or '\t' or '\r' or '\n' or ',':
                    index++;
                    continue;

                case '"':
                    index = StringEnd(text, index, end);
                    last = index;
                    continue;

                default:
                    index++;
                    last = index;
                    continue;
            }
        }

        return last;
    }

    /// <summary>Just past the string literal opening at <paramref name="index"/>, block or regular.</summary>
    static int StringEnd(string text, int index, int end)
    {
        const string block = "\"\"\"";
        if (text.AsSpan(index, end - index).StartsWith(block))
        {
            index += block.Length;
            while (index < end)
            {
                var rest = text.AsSpan(index, end - index);
                if (rest.StartsWith("\\\"\"\""))
                {
                    index += 4;
                }
                else if (rest.StartsWith(block))
                {
                    return index + block.Length;
                }
                else
                {
                    index++;
                }
            }

            return end;
        }

        index++;
        while (index < end)
        {
            switch (text[index])
            {
                case '\\':
                    index += 2;
                    break;
                case '"':
                    return index + 1;
                default:
                    index++;
                    break;
            }
        }

        return Math.Min(index, end);
    }

    /// <summary>
    /// Pulls each node's end back to <see cref="SignificantEnd"/>. Comments are their own nodes and
    /// are left alone: a comment is all insignificant, and its end is its own.
    /// </summary>
    sealed class EndTrimmer(string text) :
        ASTVisitor<EndTrimmer.Context>
    {
        public sealed class Context :
            IASTVisitorContext
        {
            public Cancel CancellationToken => default;
        }

        public override ValueTask VisitAsync(ASTNode? node, Context context)
        {
            if (node is not (null or GraphQLComment))
            {
                var (start, end) = (node.Location.Start, node.Location.End);
                var trimmed = SignificantEnd(text, start, end);
                if (trimmed != end)
                {
                    node.Location = new(start, trimmed);
                }
            }

            return base.VisitAsync(node, context);
        }
    }
}
