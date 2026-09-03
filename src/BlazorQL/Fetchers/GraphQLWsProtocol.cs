/// <summary>
/// The client side of the graphql-transport-ws protocol, as a state machine over an abstracted
/// socket: init, await ack (answering pings, ignoring keep-alives), subscribe as id "1", then yield
/// every <c>next</c> payload until <c>complete</c> or <c>error</c>. One subscription per connection.
/// </summary>
static class GraphQLWsProtocol
{
    public static async IAsyncEnumerable<JsonElement> Run(
        IWsSocket socket,
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        [EnumeratorCancellation] Cancel cancel)
    {
        await socket.SendAsync(
            JsonSerializer.Serialize(new("connection_init", headers), WebJson.Default.InitFrame),
            cancel);
        await AwaitAck(socket, cancel);
        await socket.SendAsync(
            JsonSerializer.Serialize(new("1", "subscribe", request), WebJson.Default.SubscribeFrame),
            cancel);

        while (true)
        {
            string? frame;
            try
            {
                frame = await socket.ReceiveAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                // The subscription is live; tell the server it ended before walking away.
                await SendCompleteBestEffort(socket);
                throw;
            }

            if (frame is null)
            {
                yield break;
            }

            JsonElement? next = null;
            using (var document = JsonDocument.Parse(frame))
            {
                var root = document.RootElement;
                switch (root.GetProperty("type").GetString())
                {
                    case "next":
                        next = root.GetProperty("payload").Clone();
                        break;
                    case "complete":
                        yield break;
                    case "error":
                        throw new InvalidOperationException($"Subscription failed: {root.GetProperty("payload").GetRawText()}");
                    case "ping":
                        await socket.SendAsync("""{"type":"pong"}""", cancel);
                        break;
                    default:
                        // Keep-alives ("ka") and anything unknown are ignored.
                        break;
                }
            }

            if (next is { } payload)
            {
                yield return payload;
            }
        }
    }

    static async Task AwaitAck(IWsSocket socket, Cancel cancel)
    {
        while (true)
        {
            var frame = await socket.ReceiveAsync(cancel);
            if (frame is null)
            {
                throw new InvalidOperationException("The connection closed before connection_ack.");
            }

            using var document = JsonDocument.Parse(frame);
            switch (document.RootElement.GetProperty("type").GetString())
            {
                case "connection_ack":
                    return;
                case "ping":
                    await socket.SendAsync("""{"type":"pong"}""", cancel);
                    break;
                default:
                    // Keep-alives ("ka") and anything unknown are ignored.
                    break;
            }
        }
    }

    /// <summary>
    /// Tells the server the subscription ended. The caller's token is already cancelled, hence a
    /// token of this method's own — but a bounded one: this runs inside the enumerator's disposal,
    /// which the run awaits before the stop button comes back, so a stalled socket must not be able
    /// to hold it open.
    /// </summary>
    static async Task SendCompleteBestEffort(IWsSocket socket)
    {
        try
        {
            using var timeout = new CancelSource(TimeSpan.FromSeconds(2));
            await socket.SendAsync("""{"id":"1","type":"complete"}""", timeout.Token);
        }
        catch
        {
            // Best-effort: the socket may already be gone.
        }
    }
}
