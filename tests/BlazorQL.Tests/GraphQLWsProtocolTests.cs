/// <summary>
/// The graphql-transport-ws state machine over a scripted socket: init/ack/subscribe framing, next
/// payloads until complete, pings answered with pongs, keep-alives ignored, error frames thrown,
/// and cancellation sending a best-effort complete.
/// </summary>
[TestFixture]
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

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
        Assert.That(results[1].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hola"));
        Assert.That(socket.Sent[0], Is.EqualTo("""{"type":"connection_init","payload":{"authorization":"abc"}}"""));
        Assert.That(socket.Sent[1], Is.EqualTo("""{"id":"1","type":"subscribe","payload":{"query":"subscription { message }"}}"""));
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

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(socket.Sent.Count(_ => _ == """{"type":"pong"}"""), Is.EqualTo(2));
    }

    [Test]
    public void ErrorFrameThrowsWithPayload()
    {
        var socket = new ScriptedSocket(
            """{"type":"connection_ack"}""",
            """{"id":"1","type":"error","payload":[{"message":"boom"}]}""");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => Collect(socket, new("subscription { message }"), noHeaders));

        Assert.That(exception!.Message, Does.Contain("boom"));
    }

    [Test]
    public void ClosedBeforeAckThrows()
    {
        var socket = new ScriptedSocket();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => Collect(socket, new("subscription { message }"), noHeaders));

        Assert.That(exception!.Message, Does.Contain("connection_ack"));
    }

    [Test]
    public void CancelSendsComplete()
    {
        using var cancelSource = new CancellationTokenSource();
        var socket = new HangingSocket(cancelSource);

        Assert.CatchAsync<OperationCanceledException>(() => Collect(socket, new("subscription { message }"), noHeaders, cancelSource.Token));

        Assert.That(socket.Sent[^1], Is.EqualTo("""{"id":"1","type":"complete"}"""));
    }

    static async Task<List<JsonElement>> Collect(
        IWsSocket socket,
        GraphQLRequest request,
        Dictionary<string, string> headers,
        CancellationToken cancel = default)
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

        public Task SendAsync(string json, CancellationToken cancel)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public Task<string?> ReceiveAsync(CancellationToken cancel) =>
            Task.FromResult(frames.TryDequeue(out var frame) ? frame : null);
    }

    /// <summary>
    /// Acks, then cancels the caller's own token on the next receive and hangs on it — the shape of
    /// a user stopping a live subscription mid-wait.
    /// </summary>
    sealed class HangingSocket(CancellationTokenSource cancelSource) :
        IWsSocket
    {
        bool acked;

        public List<string> Sent { get; } = [];

        public Task SendAsync(string json, CancellationToken cancel)
        {
            Sent.Add(json);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancel)
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
