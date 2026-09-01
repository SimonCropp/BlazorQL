namespace BlazorQL;

/// <summary>
/// Executes over HTTP POST. A plain JSON response yields one document; a
/// <c>multipart/mixed</c> incremental-delivery response (@defer/@stream, per deferSpec 20220824)
/// yields each part as it streams in.
/// </summary>
public sealed class HttpFetcher(HttpClient http, string url) :
    IGraphQLFetcher
{
    const string accept = "application/graphql-response+json, application/json;q=0.9, multipart/mixed;deferSpec=20220824;q=0.8";

    public HttpFetcher(string url)
        : this(new(), url)
    {
    }

    /// <summary>The endpoint every request posts to.</summary>
    public string Url { get; } = url;

    /// <summary>The current request's response status, set as soon as its headers arrive.</summary>
    public HttpFetchStatus? LastStatus { get; private set; }

    public async IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        [EnumeratorCancellation] Cancel cancel)
    {
        LastStatus = null;
        using var message = new HttpRequestMessage(HttpMethod.Post, Url);
        // Lets the browser hand back the body as it streams, instead of buffering it — a no-op
        // outside WASM, essential for multipart parts arriving over time.
        message.SetBrowserResponseStreamingEnabled(true);
        message.Headers.TryAddWithoutValidation("Accept", accept);
        foreach (var header in headers)
        {
            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var body = JsonSerializer.Serialize(request, WebJson.Default.GraphQLRequest);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel);
        LastStatus = new((int) response.StatusCode, response.ReasonPhrase);

        var contentType = response.Content.Headers.ContentType;
        if (string.Equals(contentType?.MediaType, "multipart/mixed", StringComparison.OrdinalIgnoreCase))
        {
            var boundary = contentType!.Parameters
                .FirstOrDefault(_ => string.Equals(_.Name, "boundary", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (string.IsNullOrEmpty(boundary))
            {
                throw new InvalidOperationException("The multipart/mixed response declares no boundary.");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancel);
            await using (stream)
            {
                var reader = new MultipartReader(boundary, stream);
                while (await reader.ReadNextSectionAsync(cancel) is { } section)
                {
                    using var sectionReader = new StreamReader(section.Body, Encoding.UTF8);
                    var text = await sectionReader.ReadToEndAsync(cancel);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    yield return ParseDocument(text, response);
                }
            }

            yield break;
        }

        var payload = await response.Content.ReadAsStringAsync(cancel);
        yield return ParseDocument(payload, response);
    }

    /// <summary>
    /// Parses one response document, cloned so it outlives its backing buffer. GraphQL errors ride
    /// non-success statuses as ordinary JSON bodies; only a non-JSON body is a transport failure.
    /// </summary>
    static JsonElement ParseDocument(string text, HttpResponseMessage response)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            var preview = text.Length > 500
                ? text[..500]
                : text;
            throw new InvalidOperationException($"The endpoint answered {(int) response.StatusCode} with a non-JSON body: {preview}");
        }
    }
}
