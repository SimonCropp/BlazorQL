namespace BlazorQL;

/// <summary>
/// Resolves the token at an offset into a <see cref="SchemaReference"/> — what ctrl/cmd-click
/// jump-to-doc navigates to. The context scanner supplies what surrounds the word; a field,
/// argument, or type reference that resolves against the schema is returned, anything else null.
/// </summary>
public static class SchemaReferenceResolver
{
    public static SchemaReference? Resolve(SchemaIndex schema, string text, int offset)
    {
        var (word, start, _) = HoverEngine.WordAt(text, offset);
        if (word is null)
        {
            return null;
        }

        // Context at the START of the word, so the word itself is not consumed as context.
        var scan = ContextScanner.Scan(schema, text, start);

        if (scan is
            {
                Mode: ScanMode.Selection,
                CurrentType: { } parent
            } &&
            parent.Fields?.Any(_ => _.Name == word) is true)
        {
            return new("Field", parent.Name, word);
        }

        if (scan.Mode is ScanMode.ArgumentName or ScanMode.ArgumentValue &&
            scan is
            {
                CurrentType: { } argumentParent,
                CurrentField: { } field
            } &&
            field.Args.Any(_ => _.Name == word))
        {
            return new("Argument", argumentParent.Name, field.Name, word);
        }

        if (schema.Find(word) is { } type)
        {
            return new("Type", type.Name);
        }

        return null;
    }
}
