using System.Text;

/// <summary>
/// Devtools' "Copy as PowerShell", which is an <c>Invoke-WebRequest</c> call rather than a curl
/// command: named parameters, a hashtable of headers, backtick line continuations, and backtick
/// escapes inside double-quoted strings. Its own small reader, because it shares no syntax with the
/// shell dialects beyond the idea of an argument.
/// </summary>
static class PowerShellReader
{
    public static CapturedRequest Read(string text)
    {
        var headers = new List<(string Name, string Value)>();
        string? url = null;
        string? body = null;

        var index = 0;
        while (index < text.Length)
        {
            SkipGap(text, ref index);
            if (index >= text.Length)
            {
                break;
            }

            if (text[index] != '-')
            {
                // The cmdlet name, a pipeline, a stray token: nothing this reader wants.
                SkipToken(text, ref index);
                continue;
            }

            var start = index;
            index++;
            while (index < text.Length &&
                   char.IsLetterOrDigit(text[index]))
            {
                index++;
            }

            var name = text[start..index];
            SkipGap(text, ref index);
            switch (name.ToLowerInvariant())
            {
                case "-uri":
                    url = ReadValue(text, ref index);
                    break;
                case "-body":
                    body = ReadValue(text, ref index);
                    break;
                case "-headers":
                    ReadHashtable(text, ref index, headers);
                    break;
                case "-contenttype":
                    headers.Add(("content-type", ReadValue(text, ref index) ?? ""));
                    break;
                case "-method" or "-useragent" or "-websession" or "-sessionvariable" or "-infile" or "-outfile":
                    ReadValue(text, ref index);
                    break;
                default:
                    // A switch parameter such as -UseBasicParsing takes no value, so nothing is
                    // consumed here. An unknown parameter that did take one leaves its value to be
                    // skipped as a stray token on the next turn.
                    break;
            }
        }

        return new(url, headers, body);
    }

    // Whitespace, and the backtick-newline that continues a command across lines.
    static void SkipGap(string text, ref int index)
    {
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            if (text[index] == '`' &&
                index + 1 < text.Length &&
                text[index + 1] is '\r' or '\n')
            {
                index++;
                continue;
            }

            return;
        }
    }

    static void SkipToken(string text, ref int index)
    {
        if (ReadValue(text, ref index) is not null)
        {
            return;
        }

        index++;
    }

    /// <summary>
    /// A double-quoted string (backtick escapes), a single-quoted string (doubling escapes a
    /// quote), or a bare run up to the next whitespace.
    /// </summary>
    static string? ReadValue(string text, ref int index)
    {
        if (index >= text.Length)
        {
            return null;
        }

        if (text[index] == '"')
        {
            index++;
            var builder = new StringBuilder();
            while (index < text.Length &&
                   text[index] != '"')
            {
                if (text[index] == '`' &&
                    index + 1 < text.Length)
                {
                    builder.Append(Unescape(text[index + 1]));
                    index += 2;
                    continue;
                }

                builder.Append(text[index]);
                index++;
            }

            index++;
            return builder.ToString();
        }

        if (text[index] == '\'')
        {
            index++;
            var builder = new StringBuilder();
            while (index < text.Length)
            {
                if (text[index] == '\'')
                {
                    // A doubled quote is the only escape a literal string has.
                    if (index + 1 < text.Length &&
                        text[index + 1] == '\'')
                    {
                        builder.Append('\'');
                        index += 2;
                        continue;
                    }

                    index++;
                    break;
                }

                builder.Append(text[index]);
                index++;
            }

            return builder.ToString();
        }

        var start = index;
        while (index < text.Length &&
               !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index > start
            ? text[start..index]
            : null;
    }

    static char Unescape(char escape) =>
        escape switch
        {
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            '0' => '\0',
            'a' => '\a',
            'b' => '\b',
            'f' => '\f',
            'v' => '\v',
            'e' => (char) 0x1b,
            _ => escape
        };

    /// <summary>
    /// The <c>@{ "name" = "value" ... }</c> block devtools writes the headers into. Pairs are
    /// separated by newlines rather than commas, so the reader just walks to the closing brace.
    /// </summary>
    static void ReadHashtable(string text, ref int index, List<(string Name, string Value)> headers)
    {
        if (index < text.Length &&
            text[index] == '@')
        {
            index++;
        }

        if (index >= text.Length ||
            text[index] != '{')
        {
            return;
        }

        index++;
        while (index < text.Length)
        {
            SkipGap(text, ref index);
            if (index >= text.Length ||
                text[index] == '}')
            {
                index++;
                return;
            }

            if (text[index] is ';' or ',')
            {
                index++;
                continue;
            }

            var name = ReadKey(text, ref index);
            SkipGap(text, ref index);
            if (index < text.Length &&
                text[index] == '=')
            {
                index++;
            }

            SkipGap(text, ref index);
            var value = ReadValue(text, ref index);
            if (name is {Length: > 0})
            {
                headers.Add((name, value ?? ""));
            }
        }
    }

    // A hashtable key: quoted like any value, or bare up to the equals sign.
    static string? ReadKey(string text, ref int index)
    {
        if (index < text.Length &&
            text[index] is '"' or '\'')
        {
            return ReadValue(text, ref index);
        }

        var start = index;
        while (index < text.Length &&
               text[index] != '=' &&
               !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index > start
            ? text[start..index]
            : null;
    }
}
