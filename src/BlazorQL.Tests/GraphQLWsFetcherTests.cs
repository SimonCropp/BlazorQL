using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;

/// <summary>
/// The websocket fetcher against a real socket, over a server that goes silent — which is the case
/// worth having: closing a websocket politely means waiting for the peer's close frame, and this
/// close happens inside the enumerator's disposal, which the run awaits before it hands the stop
/// button back.
/// </summary>
[TestFixture]
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
        using var server = new SilentWebSocketServer();

        var fetcher = new GraphQLWsFetcher($"ws://127.0.0.1:{server.Port}/graphql");

        var run = Task.Run(
            async () =>
            {
                List<string?> messages = [];
                await foreach (var payload in fetcher.FetchAsync(new("subscription { message }"), noHeaders, Cancel.None))
                {
                    messages.Add(payload.GetProperty("data").GetProperty("message").GetString());
                }

                return messages;
            });

        var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.That(finished, Is.SameAs(run), "the fetch never unwound");
        Assert.That(await run, Is.EqualTo(oneMessage));
    }

    static readonly string[] oneMessage = ["Hi"];

    static readonly Dictionary<string, string> noHeaders = [];

    /// <summary>
    /// A graphql-transport-ws server that acks, answers one subscribe with one event, and then says
    /// nothing ever again — not even a close frame, and without dropping the connection. Written
    /// over a raw TcpListener because the handshake is a dozen lines and the alternatives are
    /// platform-specific.
    /// </summary>
    sealed class SilentWebSocketServer :
        IDisposable
    {
        readonly TcpListener listener;
        readonly CancelSource life = new();

        public SilentWebSocketServer()
        {
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
                await Send(socket, """{"id":"1","type":"complete"}""");

                // And now nothing: no frames, no close, and the connection stays up.
                await Task.Delay(Timeout.Infinite, life.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                TestContext.Out.WriteLine($"Silent server stopped: {exception.Message}");
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

        static ValueTask Send(WebSocket socket, string json) =>
            socket.SendAsync(
                Encoding.UTF8.GetBytes(json).AsMemory(),
                WebSocketMessageType.Text,
                endOfMessage: true,
                Cancel.None);

        static async Task Receive(WebSocket socket)
        {
            var buffer = new byte[4096];
            while (true)
            {
                var result = await socket.ReceiveAsync(new(buffer), Cancel.None);
                if (result.EndOfMessage)
                {
                    return;
                }
            }
        }

        public void Dispose()
        {
            life.Cancel();
            listener.Dispose();
            life.Dispose();
        }
    }
}
