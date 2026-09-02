namespace BlazorQL;

/// <summary>
/// Hover docs from the introspection model: the field or type under the caret, its signature, and
/// its description as markdown. Reuses the completion scanner's context resolution — the word
/// under the caret is looked up in whatever the scanner says surrounds it.
/// </summary>
public static class HoverEngine
{
    public static HoverInfo? Hover(SchemaIndex schema, string text, int offset)
    {
        var (word, start, end) = WordAt(text, offset);
        if (word is null)
        {
            return null;
        }

        // Context at the START of the word, so the word itself is not consumed as context.
        var scan = ContextScanner.Scan(schema, text, start);

        // A type name anywhere it can appear.
        if (schema.Find(word) is { } type &&
            (scan.Mode is ScanMode.TypeCondition or ScanMode.VariableType or ScanMode.Document ||
             char.IsUpper(word[0])))
        {
            return new(Markdown($"{Keyword(type.Kind)} {type.Name}", type.Description), start, end);
        }

        if (scan.Mode == ScanMode.Selection &&
            scan.CurrentType?.Fields?.FirstOrDefault(_ => _.Name == word) is { } field)
        {
            var signature = $"{scan.CurrentType.Name}.{field.Name}: {field.Type.Display()}";
            var description = field.Description;
            if (field.IsDeprecated)
            {
                description = $"**Deprecated.** {field.DeprecationReason}\n\n{description}".TrimEnd();
            }

            return new(Markdown(signature, description), start, end);
        }

        if (scan.Mode is ScanMode.ArgumentName or ScanMode.ArgumentValue &&
            scan.CurrentField?.Args.FirstOrDefault(_ => _.Name == word) is { } argument)
        {
            return new(Markdown($"{argument.Name}: {argument.Type.Display()}", argument.Description), start, end);
        }

        return null;
    }

    static string Keyword(string kind) =>
        kind switch
        {
            "OBJECT" => "type",
            "INTERFACE" => "interface",
            "UNION" => "union",
            "ENUM" => "enum",
            "INPUT_OBJECT" => "input",
            "SCALAR" => "scalar",
            _ => "type"
        };

    static string Markdown(string signature, string? description)
    {
        if (description is null)
        {
            return $"```graphql\n{signature}\n```";
        }

        return $"```graphql\n{signature}\n```\n\n{description}";
    }

    internal static (string? Word, int Start, int End) WordAt(string text, int offset)
    {
        if (text.Length == 0)
        {
            return (null, 0, 0);
        }

        offset = Math.Clamp(offset, 0, text.Length - 1);
        if (!IsWordChar(text[offset]) && offset > 0 && IsWordChar(text[offset - 1]))
        {
            offset--;
        }

        if (!IsWordChar(text[offset]))
        {
            return (null, 0, 0);
        }

        var start = offset;
        while (start > 0 && IsWordChar(text[start - 1]))
        {
            start--;
        }

        var end = offset + 1;
        while (end < text.Length && IsWordChar(text[end]))
        {
            end++;
        }

        return (text[start..end], start, end);
    }

    static bool IsWordChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';
}
