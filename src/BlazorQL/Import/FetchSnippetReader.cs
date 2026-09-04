using System.Text;

/// <summary>
/// Devtools' "Copy as fetch" (and its Node.js variant). The options object is worth a special note:
/// devtools double-quotes every key and writes every value as a JSON value, so the object is valid
/// JSON and needs no JavaScript reader — only brace-matching to find where it ends.
/// </summary>
static class FetchSnippetReader
{
    public static CapturedRequest Read(string text)
    {
        var call = text.IndexOf("fetch(", StringComparison.Ordinal);
        if (call < 0)
        {
            return CapturedRequest.None;
        }

        var index = call + "fetch(".Length;
        SkipWhitespace(text, ref index);
        var url = ReadStringLiteral(text, ref index);

        var start = text.IndexOf('{', index);
        if (start < 0)
        {
            // A fetch with no options object is still a GET whose url may carry the query.
            return new(url, [], null);
        }

        var end = MatchBrace(text, start);
        if (end < 0)
        {
            return new(url, [], null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text[start..(end + 1)]);
        }
        catch (JsonException)
        {
            return new(url, [], null);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new(url, [], null);
            }

            var headers = new List<(string Name, string Value)>();
            if (root.TryGetProperty("headers", out var captured) &&
                captured.ValueKind == JsonValueKind.Object)
            {
                foreach (var header in captured.EnumerateObject())
                {
                    if (header.Value.ValueKind == JsonValueKind.String)
                    {
                        headers.Add((header.Name, header.Value.GetString() ?? ""));
                    }
                }
            }

            var body = root.TryGetProperty("body", out var value) &&
                       value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
            return new(url, headers, body);
        }
    }

    static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length &&
               char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }

    static string? ReadStringLiteral(string text, ref int index)
    {
        if (index >= text.Length ||
            text[index] is not ('"' or '\''))
        {
            return null;
        }

        var quote = text[index];
        index++;
        var builder = new StringBuilder();
        while (index < text.Length &&
               text[index] != quote)
        {
            if (text[index] == '\\' &&
                index + 1 < text.Length)
            {
                builder.Append(text[index + 1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => text[index + 1]
                });
                index += 2;
                continue;
            }

            builder.Append(text[index]);
            index++;
        }

        index++;
        return builder.ToString();
    }

    /// <summary>
    /// The index of the brace closing the one at <paramref name="start"/>, or -1. Braces inside
    /// string literals are skipped — the body is a JSON string full of them.
    /// </summary>
    static int MatchBrace(string text, int start)
    {
        var depth = 0;
        var index = start;
        while (index < text.Length)
        {
            var character = text[index];
            if (character is '"' or '\'')
            {
                var quote = character;
                index++;
                while (index < text.Length &&
                       text[index] != quote)
                {
                    index += text[index] == '\\'
                        ? 2
                        : 1;
                }

                index++;
                continue;
            }

            if (character == '{')
            {
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }

            index++;
        }

        return -1;
    }
}
