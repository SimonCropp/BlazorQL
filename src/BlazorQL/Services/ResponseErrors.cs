namespace BlazorQL;

/// <summary>One error from a response, reduced to what the pane can act on.</summary>
/// <param name="Message">The server's message, shown as-is.</param>
/// <param name="Path">
/// The response path with its list indices dropped. A list's selection set is written once in the
/// document however many elements come back, so the indices identify a datum rather than a field.
/// </param>
public sealed record ResponseError(string Message, IReadOnlyList<string> Path)
{
    /// <summary>The path as the response spells it, for showing next to the message.</summary>
    public string PathText { get; } = string.Join(".", Path);

    /// <summary>
    /// Whether this error names a field, which is what the remove action needs. An error raised
    /// before execution began — a validation failure, a bad variable — has no path at all.
    /// </summary>
    public bool HasPath => Path.Count > 0;
}

/// <summary>
/// Reads the <c>errors</c> array of a response document.
/// </summary>
/// <remarks>
/// <c>path</c> is the anchor rather than <c>locations</c>: it survives a document that mentions the
/// same field twice, and it is what the spec asks a server to send for a field that failed. A server
/// that sends neither leaves the error informational, which is the state a scrubbed error list ends
/// up in.
/// </remarks>
public static class ResponseErrors
{
    public static IReadOnlyList<ResponseError> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. errors.EnumerateArray().Select(Read).OfType<ResponseError>()];
        }
        catch (JsonException)
        {
            // The pane holds whatever came back, which is not always json — a proxy's html error
            // page, most often.
            return [];
        }
    }

    static ResponseError? Read(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var message = error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? ""
            : "";
        if (message.Length == 0)
        {
            return null;
        }

        return new(message, Path(error));
    }

    static IReadOnlyList<string> Path(JsonElement error)
    {
        if (!error.TryGetProperty("path", out var path) ||
            path.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. path.EnumerateArray()
                .Where(_ => _.ValueKind == JsonValueKind.String)
                .Select(_ => _.GetString()!)
        ];
    }
}
