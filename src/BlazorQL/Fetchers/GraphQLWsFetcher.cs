namespace BlazorQL;

/// <summary>
/// Executes over a graphql-transport-ws websocket — the transport subscriptions ride. Each fetch
/// opens its own connection, runs one subscription (id "1"), and closes on the way out. The
/// request headers travel in the <c>connection_init</c> payload, where the protocol puts them.
/// </summary>
public sealed class GraphQLWsFetcher(string url) :
    IGraphQLFetcher
{
    /// <summary>The ws(s):// endpoint every fetch connects to.</summary>
    public string Url { get; } = url;

    public async IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        [EnumeratorCancellation] Cancel cancel)
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("graphql-transport-ws");
        await socket.ConnectAsync(new(Url), cancel);
        try
        {
            await foreach (var payload in GraphQLWsProtocol.Run(new ClientWebSocketAdapter(socket), request, headers, cancel))
            {
                yield return payload;
            }
        }
        finally
        {
            await CloseBestEffort(socket);
        }
    }

    static async Task CloseBestEffort(ClientWebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", Cancel.None);
        }
        catch (WebSocketException)
        {
            // Best-effort: the server may have dropped the connection already.
        }
    }
}
