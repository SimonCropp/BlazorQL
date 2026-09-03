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

        var body = JsonSerializer.Serialize(request, WebJson.Default.GraphQLRequest);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // The negotiated Accept is a default, not a floor. Appending the user's after it would
        // leave the endpoint to choose between the two, which is not what typing one means.
        if (!headers.Keys.Any(_ => string.Equals(_, "Accept", StringComparison.OrdinalIgnoreCase)))
        {
            message.Headers.TryAddWithoutValidation("Accept", accept);
        }

        foreach (var header in headers)
        {
            if (message.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                continue;
            }

            // A content header (Content-Type and the rest) is refused by the request's collection,
            // and refused silently — so it goes where it belongs instead of being dropped. Removing
            // first, because StringContent has already written a Content-Type of its own.
            message.Content.Headers.Remove(header.Key);
            message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancel);
        LastStatus = new((int) response.StatusCode, response.ReasonPhrase);

        var contentType = response.Content.Headers.ContentType;
        if (string.Equals(contentType?.MediaType, "multipart/mixed", StringComparison.OrdinalIgnoreCase))
        {
            if (!response.Content.TryGetMultipartBoundary(out var boundary))
            {
                throw new InvalidOperationException("The multipart/mixed response declares no boundary.");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancel);
            await using (stream)
            {
                using var reader = new MultipartReader(boundary, stream);
                while (await reader.ReadNextSectionAsync(cancel) is { } section)
                {
                    using var buffered = new MemoryStream();
                    await section.Body.CopyToAsync(buffered, cancel);
                    if (IsBlank(buffered))
                    {
                        continue;
                    }

                    yield return ParseDocument(buffered, response);
                }
            }

            yield break;
        }

        var payload = await response.Content.ReadAsStreamAsync(cancel);
        await using (payload)
        {
            using var buffered = new MemoryStream();
            await payload.CopyToAsync(buffered, cancel);
            yield return ParseDocument(buffered, response);
        }
    }

    /// <summary>Whether a buffered part holds nothing but whitespace — a boundary's trailing newline.</summary>
    static bool IsBlank(MemoryStream buffered)
    {
        foreach (var value in buffered.GetBuffer().AsSpan(0, (int) buffered.Length))
        {
            if (value is not ((byte) ' ' or (byte) '\t' or (byte) '\r' or (byte) '\n'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Parses one response document, cloned so it outlives its backing buffer. Read from the bytes
    /// rather than from a string: a large response was otherwise copied into utf-16 and then parsed
    /// out of that again. GraphQL errors ride non-success statuses as ordinary JSON bodies; only a
    /// non-JSON body is a transport failure, and only that path pays for the text.
    /// </summary>
    static JsonElement ParseDocument(MemoryStream buffered, HttpResponseMessage response)
    {
        var bytes = buffered.GetBuffer().AsSpan(0, (int) buffered.Length);
        try
        {
            var reader = new Utf8JsonReader(bytes);
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            var preview = Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 500)]);
            throw new InvalidOperationException($"The endpoint answered {(int) response.StatusCode} with a non-JSON body: {preview}");
        }
    }
}
