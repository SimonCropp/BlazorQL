/// <summary>
/// Turns a copied curl command back into its arguments. Bash and cmd get separate walks on purpose:
/// their quoting rules have nothing in common beyond "whitespace separates arguments", and one
/// state machine covering both would branch on every character for no shared code.
/// </summary>
static class ShellTokenizer
{
    /// <summary>
    /// Bash quoting: backslash escapes, literal single-quoted runs, double-quoted runs with a few
    /// escapes, and ANSI-C <c>$'...'</c> runs — which devtools emits whenever a header or body holds
    /// a control character. Adjacent runs concatenate into one word, which is what makes bash's
    /// <c>'\''</c> idiom for an embedded quote work without a special case.
    /// </summary>
    public static List<string> TokenizeBash(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var started = false;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character))
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                index++;
                continue;
            }

            if (character == '\\' &&
                index + 1 < text.Length)
            {
                // A backslash before a newline continues the line; before anything else it makes
                // that character literal.
                var after = SkipNewline(text, index + 1);
                if (after > index + 1)
                {
                    index = after;
                    continue;
                }

                current.Append(text[index + 1]);
                started = true;
                index += 2;
                continue;
            }

            if (character == '\'')
            {
                started = true;
                index++;
                while (index < text.Length &&
                       text[index] != '\'')
                {
                    current.Append(text[index]);
                    index++;
                }

                index++;
                continue;
            }

            if (character == '"')
            {
                started = true;
                index++;
                while (index < text.Length &&
                       text[index] != '"')
                {
                    if (text[index] == '\\' &&
                        index + 1 < text.Length &&
                        text[index + 1] is '"' or '\\' or '$' or '`')
                    {
                        current.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }

                    current.Append(text[index]);
                    index++;
                }

                index++;
                continue;
            }

            if (character == '$' &&
                index + 1 < text.Length &&
                text[index + 1] == '\'')
            {
                started = true;
                index = AppendAnsiC(text, index + 2, current);
                continue;
            }

            current.Append(character);
            started = true;
            index++;
        }

        if (started)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// The cmd flavour, which devtools produces by backslash-escaping quotes and backslashes and
    /// then caret-escaping every character outside a small allowlist. The ambiguity to respect is
    /// that a caret-quote is both the argument delimiter and the tail of an escaped inner quote
    /// (a quote becomes backslash-quote becomes caret-backslash-caret-quote), so the four-character
    /// forms have to be matched before the two-character one.
    /// </summary>
    public static List<string> TokenizeCmd(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var started = false;
        var quoted = false;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '^' &&
                index + 1 < text.Length)
            {
                // Both of these decode to one literal character, and both begin with a caret and a
                // backslash. Matching them ahead of the delimiter is what keeps a value containing
                // a quote or a backslash intact.
                if (Matches(text, index, "^\\^\""))
                {
                    current.Append('"');
                    started = true;
                    index += 4;
                    continue;
                }

                if (Matches(text, index, "^\\^\\"))
                {
                    current.Append('\\');
                    started = true;
                    index += 4;
                    continue;
                }

                if (text[index + 1] == '"')
                {
                    quoted = !quoted;
                    started = true;
                    index += 2;
                    continue;
                }

                var afterFirst = SkipNewline(text, index + 1);
                if (afterFirst > index + 1)
                {
                    // A newline inside a value is emitted doubled; a single one is the continuation
                    // between arguments and carries nothing.
                    var afterSecond = SkipNewline(text, afterFirst);
                    if (afterSecond > afterFirst)
                    {
                        current.Append('\n');
                        started = true;
                        index = afterSecond;
                        continue;
                    }

                    index = afterFirst;
                    continue;
                }

                current.Append(text[index + 1]);
                started = true;
                index += 2;
                continue;
            }

            if (!quoted &&
                char.IsWhiteSpace(character))
            {
                if (started)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                index++;
                continue;
            }

            current.Append(character);
            started = true;
            index++;
        }

        if (started)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    static bool Matches(string text, int index, string pattern)
    {
        if (index + pattern.Length > text.Length)
        {
            return false;
        }

        for (var offset = 0; offset < pattern.Length; offset++)
        {
            if (text[index + offset] != pattern[offset])
            {
                return false;
            }
        }

        return true;
    }

    // The index past a newline at this position, or the position itself when there is none.
    static int SkipNewline(string text, int index)
    {
        if (index >= text.Length)
        {
            return index;
        }

        if (text[index] == '\r')
        {
            index++;
            if (index < text.Length &&
                text[index] == '\n')
            {
                index++;
            }

            return index;
        }

        if (text[index] == '\n')
        {
            return index + 1;
        }

        return index;
    }

    // An ANSI-C run, from just past the opening quote to just past the closing one.
    static int AppendAnsiC(string text, int index, StringBuilder current)
    {
        while (index < text.Length &&
               text[index] != '\'')
        {
            if (text[index] != '\\' ||
                index + 1 >= text.Length)
            {
                current.Append(text[index]);
                index++;
                continue;
            }

            index++;
            var escape = text[index];
            index++;
            switch (escape)
            {
                case 'n':
                    current.Append('\n');
                    break;
                case 'r':
                    current.Append('\r');
                    break;
                case 't':
                    current.Append('\t');
                    break;
                case 'a':
                    current.Append('\a');
                    break;
                case 'b':
                    current.Append('\b');
                    break;
                case 'f':
                    current.Append('\f');
                    break;
                case 'v':
                    current.Append('\v');
                    break;
                case 'e':
                    current.Append((char) 0x1b);
                    break;
                case 'x':
                    index = AppendHex(text, index, current, 2);
                    break;
                case 'u':
                    index = AppendHex(text, index, current, 4);
                    break;
                case 'U':
                    index = AppendHex(text, index, current, 8);
                    break;
                default:
                    // Octal, and anything else, is close enough taken literally: devtools only ever
                    // emits the forms above, and a wrong guess here would corrupt a byte rather
                    // than fail loudly.
                    current.Append(escape);
                    break;
            }
        }

        return index + 1;
    }

    static int AppendHex(string text, int index, StringBuilder current, int maxDigits)
    {
        var value = 0;
        var digits = 0;
        while (digits < maxDigits &&
               index < text.Length &&
               Uri.IsHexDigit(text[index]))
        {
            value = value * 16 + Uri.FromHex(text[index]);
            index++;
            digits++;
        }

        if (digits == 0)
        {
            return index;
        }

        // A lone surrogate is not a scalar value, so it cannot go through ConvertFromUtf32 — but it
        // is still exactly the char that was asked for.
        if (value is >= 0xD800 and <= 0xDFFF ||
            value > 0x10FFFF)
        {
            current.Append((char) value);
            return index;
        }

        current.Append(char.ConvertFromUtf32(value));
        return index;
    }
}
