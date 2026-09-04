namespace BlazorQL;

/// <summary>
/// Prettify, in pure C#: GraphQL through GraphQLParser's printer, JSONC through System.Text.Json.
/// A text that does not parse comes back unchanged — the button never destroys work in progress.
/// Note the JSON pass drops comments; GraphQL comments survive.
/// </summary>
public static class Formatter
{
    static SDLPrinter printer = new(new()
    {
        PrintComments = true
    });

    static JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static JsonDocumentOptions jsoncOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string FormatGraphQL(string text)
    {
        var document = DocumentInfo.Parse(text);
        if (document.Document is null)
        {
            return text;
        }

        var writer = new StringWriter();
        printer.PrintAsync(document.Document, writer).AsTask().GetAwaiter().GetResult();
        var formatted = writer.ToString();
        return formatted.EndsWith('\n') ? formatted : formatted + '\n';
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "JsonElement has a built-in converter, so nothing here is discovered by reflection.")]
    public static string FormatJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        try
        {
            using var document = JsonDocument.Parse(text, jsoncOptions);
            return JsonSerializer.Serialize(document.RootElement, jsonOptions) + "\n";
        }
        catch (JsonException)
        {
            return text;
        }
    }

    /// <summary>
    /// Parses JSONC (comments and trailing commas tolerated) into a value, refusing a non-object
    /// root the way GraphiQL's tryParseJSONC does. Ok=false carries the message for the response
    /// pane; a null value with Ok=true means empty input.
    /// </summary>
    public static (bool Ok, JsonElement? Value, string? Error) ParseJsonc(string text, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (true, null, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, jsoncOptions);
        }
        catch (JsonException exception)
        {
            return (false, null, $"{what} are invalid JSON: {exception.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, null, $"{what} are not a JSON object.");
            }

            // Cloned so the document can go. Its element points into a pooled buffer that
            // JsonDocument rents and only returns on dispose, and this runs on every diagnostics
            // pass and every run — returning the element live meant never giving the buffer back.
            return (true, document.RootElement.Clone(), null);
        }
    }
}
