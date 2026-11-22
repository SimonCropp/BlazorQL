/// <summary>
/// Reads the GraphQL payload a GET puts in its query string, or a form-encoded body. A plus sign is
/// a space and the rest is percent-decoded, the way form decoding reads it. That is safe for every
/// encoder in use: encodeURIComponent writes a plus as <c>%2B</c>, so a raw one only ever comes from
/// form-style encoding (URLSearchParams, an HTML form, jQuery, Java's URLEncoder), where it means a
/// space.
/// </summary>
static class RequestUrlReader
{
    public static (GraphQLPayload? Payload, string? Error) Read(string url)
    {
        var question = url.IndexOf('?');
        return question < 0
            ? (null, "That url has no query string, so there is no request in it to import.")
            : ReadParameters(url[(question + 1)..]);
    }

    public static (GraphQLPayload? Payload, string? Error) ReadParameters(string queryString)
    {
        string? query = null;
        string? variables = null;
        string? operationName = null;
        var persisted = false;

        foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var name = Decode(pair[..equals]);
            var value = Decode(pair[(equals + 1)..]);
            switch (name)
            {
                case "query":
                    query = value;
                    break;
                case "variables":
                    variables = value;
                    break;
                case "operationName":
                    operationName = value;
                    break;
                case "extensions":
                    persisted = value.Contains("persistedQuery", StringComparison.Ordinal);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return persisted
                ? (null, RequestBodyReader.PersistedQuery)
                : (null, "That looks like a request, but it carries no GraphQL query.");
        }

        return (new(query, RequestBodyReader.Meaningful(variables), operationName), null);
    }

    // The plus first: an encoded one, %2B, decodes to a plus after it.
    static string Decode(string component) =>
        Uri.UnescapeDataString(component.Replace('+', ' '));
}
