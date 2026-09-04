/// <summary>
/// Reads the GraphQL payloads out of a request body. Three shapes reach here: the JSON object every
/// client sends, a JSON array when a client batches, and — rarely — a form-encoded body or a raw
/// <c>application/graphql</c> document.
/// </summary>
static class RequestBodyReader
{
    public const string PersistedQuery =
        "This is a persisted query. Only its hash was sent, so the document cannot be recovered.";

    const string noQuery = "That looks like a request, but it carries no GraphQL query.";

    public static (List<GraphQLPayload> Payloads, string? Error) Read(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return ([], noQuery);
        }

        var trimmed = body.Trim();
        if (trimmed[0] is '{' or '[')
        {
            return ReadJson(trimmed);
        }

        // A form-encoded body puts the same members in query-string parameters.
        if (trimmed.Contains("query=", StringComparison.Ordinal))
        {
            var (payload, error) = RequestUrlReader.ReadParameters(trimmed);
            return payload is null
                ? ([], error)
                : ([payload], null);
        }

        // An application/graphql body is the document itself.
        if (DocumentInfo.Parse(trimmed).Parses)
        {
            return ([new(trimmed, null, null)], null);
        }

        return ([], noQuery);
    }

    static (List<GraphQLPayload> Payloads, string? Error) ReadJson(string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // A brace can open a JSON object or an anonymous GraphQL query, and only one of them
            // parses. Trying the other before reporting a JSON error turns a confusing message into
            // a working import for anyone who pasted the document itself.
            return DocumentInfo.Parse(body).Operations.Count > 0
                ? ([new(body, null, null)], null)
                : ([], "The request body is not valid JSON.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var (payload, error) = ReadObject(root);
                return payload is null
                    ? ([], error)
                    : ([payload], null);
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return ([], noQuery);
            }

            var payloads = new List<GraphQLPayload>();
            string? firstError = null;
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var (payload, error) = ReadObject(element);
                if (payload is null)
                {
                    firstError ??= error;
                    continue;
                }

                payloads.Add(payload);
            }

            // A batch where one entry is unreadable still imports the rest; a batch where none are
            // readable reports why the first one failed.
            return payloads.Count > 0
                ? (payloads, null)
                : ([], firstError ?? noQuery);
        }
    }

    static (GraphQLPayload? Payload, string? Error) ReadObject(JsonElement element)
    {
        if (!element.TryGetProperty("query", out var query) ||
            query.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(query.GetString()))
        {
            // An automatic persisted query sends a hash in place of the document. Saying so is the
            // one case the user cannot fix by copying the request again.
            if (element.TryGetProperty("extensions", out var extensions) &&
                extensions.ValueKind == JsonValueKind.Object &&
                extensions.TryGetProperty("persistedQuery", out _))
            {
                return (null, PersistedQuery);
            }

            return (null, noQuery);
        }

        string? variables = null;
        if (element.TryGetProperty("variables", out var captured))
        {
            // Most clients send an object; some send the object as a JSON string, the way a GET url
            // has to. Both end up as the same text in the variables pane.
            var text = captured.ValueKind switch
            {
                JsonValueKind.Object => captured.GetRawText(),
                JsonValueKind.String => captured.GetString(),
                _ => null
            };
            variables = Meaningful(text);
        }

        var operationName = element.TryGetProperty("operationName", out var name) &&
                            name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
        return (new(query.GetString()!, variables, operationName), null);
    }

    /// <summary>
    /// Variables worth putting in the pane. An absent, null, or empty object comes back null rather
    /// than as a pair of braces, which would pop the tools strip open over nothing.
    /// </summary>
    public static string? Meaningful(string? variables)
    {
        if (string.IsNullOrWhiteSpace(variables))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(variables);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.EnumerateObject().Any()
                ? variables
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
