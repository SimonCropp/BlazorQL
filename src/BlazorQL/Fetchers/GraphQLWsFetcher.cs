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

    /// <summary>
    /// Sends the close frame on the way out, without waiting for the server's answer.
    /// <see cref="ClientWebSocket.CloseAsync"/> waits for that answer, and with no token that wait
    /// has no end — all of which happens inside the enumerator's disposal, which the run awaits
    /// before the stop button comes back. The socket is disposed either way, so the courtesy of the
    /// frame is worth a bounded moment and no more.
    /// </summary>
    static async Task CloseBestEffort(ClientWebSocket socket)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            using var timeout = new CancelSource(closeTimeout);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        }
        catch (WebSocketException)
        {
            // Best-effort: the server may have dropped the connection already.
        }
        catch (OperationCanceledException)
        {
            // The frame did not go out in time. Nothing else is waiting on it.
        }
    }

    static readonly TimeSpan closeTimeout = TimeSpan.FromSeconds(2);
}
