using GraphQLParser.Visitors;

namespace BlazorQL;

/// <summary>
/// Prettify, in pure C#: GraphQL through GraphQLParser's printer, JSONC through System.Text.Json.
/// A text that does not parse comes back unchanged — the button never destroys work in progress.
/// Note the JSON pass drops comments; GraphQL comments survive.
/// </summary>
public static class Formatter
{
    static readonly SDLPrinter printer = new(new()
    {
        PrintComments = true
    });

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    static readonly JsonDocumentOptions jsoncOptions = new()
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

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            return (false, null, $"{what} are not a JSON object.");
        }

        return (true, document.RootElement, null);
    }
}
