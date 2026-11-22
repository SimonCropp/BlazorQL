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
            // The receive is not handed the caller's token: cancelling a pending receive aborts a
            // ClientWebSocket, and the complete frame below then has no socket to go out on. The
            // wait is cancelled instead, and the receive left to end when the socket is closed.
            var receive = socket.ReceiveAsync(Cancel.None);
            string? frame;
            try
            {
                frame = await receive.WaitAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Observe(receive);
                // The subscription is live; tell the server it ended before walking away.
                await SendCompleteBestEffort(socket);
                throw;
            }

            if (frame is null)
            {
                // Only complete or error ends a subscription. A close before either is the
                // connection ending under it, which is not the same as the stream finishing.
                throw new InvalidOperationException("The connection closed before the subscription completed.");
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

    /// <summary>
    /// Observes a receive nothing is waiting on any more. Closing the socket ends it, usually with an
    /// exception, which would otherwise surface as an unobserved task exception.
    /// </summary>
    static void Observe(Task receive) =>
        receive.ContinueWith(
            static _ => _.Exception,
            Cancel.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

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
