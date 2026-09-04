/// <summary>
/// Reads the GraphQL payload a GET puts in its query string. Percent-decoding only: clients build
/// these with encodeURIComponent, which leaves a plus sign alone, so treating one as a space (the
/// way form decoding would) corrupts every value that contains one.
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

            var name = Uri.UnescapeDataString(pair[..equals]);
            var value = Uri.UnescapeDataString(pair[(equals + 1)..]);
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
}
