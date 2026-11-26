using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
/// <summary>
/// The websocket fetcher against a real socket, which is where the behaviour that matters lives:
/// a server that goes silent (closing politely means waiting for the peer's close frame, inside the
/// enumerator's disposal, which the run awaits before it hands the stop button back), a connection
/// that drops or closes with an error, and a stop that has to reach the server.
/// </summary>
public class GraphQLWsFetcherTests
{
    /// <summary>
    /// The subscription ends on its own, so the socket is still open when the enumerator disposes
    /// and the close frame goes out for real. Waiting for the server's answer to it is what used to
    /// hang, and this server never sends one.
    /// </summary>
    [Test]
    public async Task AFinishedSubscriptionUnwindsThoughTheServerNeverAnswersTheClose()
    {
        // And then nothing: no frames, no close, and the connection stays up.
        using var server = new ScriptedWebSocketServer(
            async (socket, _, life) =>
            {
                await Send(socket, """{"id":"1","type":"complete"}""");
                await Task.Delay(Timeout.Infinite, life);
            });

        var run = Task.Run(() => Messages(server, Cancel.None));

        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));

        await Assert.That(finished).IsSameReferenceAs(run).Because("the fetch never unwound");
        await Assert.That(await run).IsEquivalentTo(oneMessage, CollectionOrdering.Matching);
    }

    /// <summary>
    /// A connection lost mid-subscription is a failure. Reading it as a close ended the run as
    /// though the stream had finished, with nothing to say the server had gone.
    /// </summary>
    [Test]
    public async Task ADroppedConnectionFailsTheSubscription()
    {
        using var server = new ScriptedWebSocketServer(
            (_, client, _) =>
            {
                // No close frame: the TCP connection just goes.
                client.Client.LingerState = new(true, 0);
                client.Close();
                return Task.CompletedTask;
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Messages(server, Cancel.None));

        await Assert.That(exception!.Message).Contains("The connection dropped");
    }

    /// <summary>A close that is not a normal closure carries the server's reason, and is a failure.</summary>
    [Test]
    public async Task AnErrorCloseFailsTheSubscriptionWithItsCode()
    {
        using var server = new ScriptedWebSocketServer(
            (socket, _, _) =>
                socket.CloseOutputAsync(WebSocketCloseStatus.InternalServerError, "resolver crashed", Cancel.None));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Messages(server, Cancel.None));

        await Assert.That(exception!.Message).Contains("1011").And.Contains("resolver crashed");
    }

    /// <summary>
    /// Stopping a live subscription tells the server it ended. Cancelling the pending receive
    /// aborted the socket first, so the complete frame never reached the server.
    /// </summary>
    [Test]
    public async Task StoppingSendsCompleteToTheServer()
    {
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new ScriptedWebSocketServer(
            async (socket, _, _) =>
            {
                try
                {
                    received.TrySetResult(await Receive(socket));
                }
                catch (Exception exception)
                {
                    received.TrySetResult($"no frame: {exception.GetType().Name}");
                }
            });
        using var stop = new CancelSource();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
            {
                await foreach (var _ in Fetcher(server).FetchAsync(new("subscription { message }"), noHeaders, stop.Token))
                {
                    await stop.CancelAsync();
                }
            });

        var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(20), stop.Token);
        await Assert.That(frame).IsEqualTo("""{"id":"1","type":"complete"}""");
    }

    static GraphQLWsFetcher Fetcher(ScriptedWebSocketServer server) =>
        new($"ws://127.0.0.1:{server.Port}/graphql");

    static async Task<List<string?>> Messages(ScriptedWebSocketServer server, Cancel cancel)
    {
        List<string?> messages = [];
        await foreach (var payload in Fetcher(server).FetchAsync(new("subscription { message }"), noHeaders, cancel))
        {
            messages.Add(payload.GetProperty("data").GetProperty("message").GetString());
        }

        return messages;
    }

    static ValueTask Send(WebSocket socket, string json) =>
        socket.SendAsync(
            Encoding.UTF8.GetBytes(json).AsMemory(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            Cancel.None);

    /// <summary>The next text message. A close frame instead is a failure, since every caller waits on a message.</summary>
    static async Task<string> Receive(WebSocket socket)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(new(buffer), Cancel.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("The client closed instead.");
            }

            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(message.ToArray());
            }
        }
    }

    static readonly string?[] oneMessage = ["Hi"];

    static readonly Dictionary<string, string> noHeaders = [];

    /// <summary>
    /// A graphql-transport-ws server that acks, answers one subscribe with one event, and then runs
    /// the test's script against the socket and the raw connection under it. Written over a raw
    /// TcpListener because the handshake is a dozen lines and the alternatives are
    /// platform-specific — and because dropping the connection without a close frame needs the
    /// connection itself.
    /// </summary>
    sealed class ScriptedWebSocketServer :
        IDisposable
    {
        readonly TcpListener listener;
        readonly CancelSource life = new();
        readonly Func<WebSocket, TcpClient, Cancel, Task> script;

        public ScriptedWebSocketServer(Func<WebSocket, TcpClient, Cancel, Task> script)
        {
            this.script = script;
            listener = new(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint) listener.LocalEndpoint).Port;
            _ = Task.Run(Serve);
        }

        public int Port { get; }

        async Task Serve()
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(life.Token);
                await using var stream = client.GetStream();
                await Handshake(stream);

                using var socket = WebSocket.CreateFromStream(
                    stream,
                    new()
                    {
                        IsServer = true,
                        SubProtocol = "graphql-transport-ws"
                    });

                // connection_init, then subscribe.
                await Receive(socket);
                await Send(socket, """{"type":"connection_ack"}""");
                await Receive(socket);
                await Send(socket, """{"id":"1","type":"next","payload":{"data":{"message":"Hi"}}}""");

                await script(socket, client, life.Token);
                // Held open until the test is done with it, so the end of the script is not
                // itself a drop.
                await Task.Delay(Timeout.Infinite, life.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Console.Out.WriteLine($"Test server stopped: {exception.Message}");
            }
        }

        static async Task Handshake(NetworkStream stream)
        {
            var request = new StringBuilder();
            var buffer = new byte[1];
            while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(buffer) == 0)
                {
                    throw new InvalidOperationException("The client went away mid-handshake.");
                }

                request.Append((char) buffer[0]);
            }

            var key = Regex.Match(request.ToString(), "Sec-WebSocket-Key: (.+)\r\n").Groups[1].Value.Trim();
            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

            var response = Encoding.UTF8.GetBytes(
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Protocol: graphql-transport-ws\r\n" +
                $"Sec-WebSocket-Accept: {accept}\r\n\r\n");
            await stream.WriteAsync(response);
            await stream.FlushAsync();
        }

        public void Dispose()
        {
            life.Cancel();
            listener.Dispose();
            life.Dispose();
        }
    }
}
