using System.Net.WebSockets;

/// <summary>
/// Adapts <see cref="ClientWebSocket"/> to <see cref="IWsSocket"/>: text frames only, with
/// fragmented messages reassembled into one string per receive.
/// </summary>
sealed class ClientWebSocketAdapter(ClientWebSocket socket) :
    IWsSocket
{
    public async Task SendAsync(string json, Cancel cancel) =>
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(json).AsMemory(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancel);

    public async Task<string?> ReceiveAsync(Cancel cancel)
    {
        var buffer = new byte[1024 * 4];
        using var builder = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new(buffer), cancel);
            }
            catch (WebSocketException)
            {
                // An abruptly dropped connection reads as a close.
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            builder.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(builder.GetBuffer(), 0, (int) builder.Length);
            }
        }
    }
}
