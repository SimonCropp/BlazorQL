/// <summary>
/// The graphql-transport-ws state machine over a scripted socket: init/ack/subscribe framing, next
/// payloads until complete, pings answered with pongs, keep-alives ignored, error frames thrown,
/// and cancellation sending a best-effort complete.
/// </summary>
public class GraphQLWsProtocolTests
{
    static readonly Dictionary<string, string> noHeaders = [];

    [Test]
    public async Task AckThenNextsThenComplete()
    {
        var socket = new ScriptedSocket(
            """{"type":"connection_ack"}""",
            """{"id":"1","type":"next","payload":{"data":{"message":"Hi"}}}""",
            """{"id":"1","type":"next","payload":{"data":{"message":"Hola"}}}""",
            """{"id":"1","type":"complete"}""");

        var results = await Collect(socket, new("subscription { message }"), new() {["authorization"] = "abc"});

        await Assert.That(results).Count().IsEqualTo(2);
        await Assert.That(results[0].GetProperty("data").GetProperty("message").GetString()).IsEqualTo("Hi");
        await Assert.That(results[1].GetProperty("data").GetProperty("message").GetString()).IsEqualTo("Hola");
        await Assert.That(socket.Sent[0]).IsEqualTo("""{"type":"connection_init","payload":{"authorization":"abc"}}""");
        await Assert.That(socket.Sent[1]).IsEqualTo("""{"id":"1","type":"subscribe","payload":{"query":"subscription { message }"}}""");
    }

    [Test]
    public async Task PingsGetPongsAndKeepAlivesAreIgnored()
    {
        var socket = new ScriptedSocket(
            """{"type":"ping"}""",
            """{"type":"ka"}""",
            """{"type":"connection_ack"}""",
            """{"type":"ka"}""",
            """{"type":"ping"}""",
            """{"id":"1","type":"next","payload":{"data":{"message":"Hi"}}}""",
            """{"id":"1","type":"complete"}""");

        var results = await Collect(socket, new("subscription { message }"), noHeaders);

        await Assert.That(results).Count().IsEqualTo(1);
        await Assert.That(socket.Sent.Count(_ => _ == """{"type":"pong"}""")).IsEqualTo(2);
    }

    [Test]
    public async Task ErrorFrameThrowsWithPayload()
    {
        var socket = new ScriptedSocket(
            """{"type":"connection_ack"}""",
            """{"id":"1","type":"error","payload":[{"message":"boom"}]}""");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(socket, new("subscription { message }"), noHeaders));

        await Assert.That(exception!.Message).Contains("boom");
    }

    [Test]
    public async Task ClosedBeforeAckThrows()
    {
        var socket = new ScriptedSocket();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Collect(socket, new("subscription { message }"), noHeaders));

        await Assert.That(exception!.Message).Contains("connection_ack");
    }

    [Test]
    public async Task CancelSendsComplete()
    {
        using var cancelSource = new CancelSource();
        var socket = new HangingSocket(cancelSource);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Collect(socket, new("subscription { message }"), noHeaders, cancelSource.Token));

        await Assert.That(socket.Sent[^1]).IsEqualTo("""{"id":"1","type":"complete"}""");
    }

    static async Task<List<JsonElement>> Collect(
        IWsSocket socket,
        GraphQLRequest request,
        Dictionary<string, string> headers,
        Cancel cancel = default)
    {
        List<JsonElement> results = [];
        await foreach (var element in GraphQLWsProtocol.Run(socket, request, headers, cancel))
        {
            results.Add(element);
        }

        return results;
    }

    sealed class ScriptedSocket(params string[] frames) :
        IWsSocket
    {
        readonly Queue<string> frames = new(frames);

        public List<string> Sent { get; } = [];

        public Task SendAsync(string json, Cancel cancel)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public Task<string?> ReceiveAsync(Cancel cancel) =>
            Task.FromResult(frames.TryDequeue(out var frame) ? frame : null);
    }

    /// <summary>
    /// Acks, then cancels the caller's own token on the next receive and hangs on it — the shape of
    /// a user stopping a live subscription mid-wait.
    /// </summary>
    sealed class HangingSocket(CancelSource cancelSource) :
        IWsSocket
    {
        bool acked;

        public List<string> Sent { get; } = [];

        public Task SendAsync(string json, Cancel cancel)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(Cancel cancel)
        {
            if (!acked)
            {
                acked = true;
                return """{"type":"connection_ack"}""";
            }

            await cancelSource.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancel);
            return null;
        }
    }

    /// <summary>
    /// Stopping a subscription runs inside the enumerator's disposal, which the run awaits before
    /// the stop button comes back. A socket that will not take the complete frame must not be able
    /// to hold that open.
    /// </summary>
    [Test]
    public async Task AStalledCompleteDoesNotHoldTheEnumeratorOpen()
    {
        using var cancelSource = new CancelSource();
        var socket = new StalledSendSocket(cancelSource);

        // The failsafe gets a source of its own. cancelSource is the token the socket cancels
        // itself, so sharing it would complete the delay at the same moment the run is released and
        // leave the two racing rather than giving the run twenty seconds to finish.
        using var failsafe = new CancelSource();
        var run = Task.Run(() => Collect(socket, new("subscription { message }"), noHeaders, cancelSource.Token), cancelSource.Token);
        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20), failsafe.Token));
        await failsafe.CancelAsync();

        await Assert.That(finished).IsSameReferenceAs(run).Because("the enumerator's disposal never returned");
        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
    }

    /// <summary>
    /// Acks, then cancels the caller's token and hangs on the receive — and then hangs on the
    /// complete frame too, the way a socket whose peer has stopped reading does.
    /// </summary>
    sealed class StalledSendSocket(CancelSource cancelSource) :
        IWsSocket
    {
        bool acked;

        public Task SendAsync(string json, Cancel cancel)
        {
            if (json.Contains("complete", StringComparison.Ordinal))
            {
                return Task.Delay(Timeout.Infinite, cancel);
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(Cancel cancel)
        {
            if (!acked)
            {
                acked = true;
                return """{"type":"connection_ack"}""";
            }

            await cancelSource.CancelAsync();
            await Task.Delay(Timeout.Infinite, cancel);
            return null;
        }
    }
}