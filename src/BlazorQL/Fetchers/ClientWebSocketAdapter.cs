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
            catch (WebSocketException exception)
            {
                // A dropped connection is a failure, not a close: reading it as one ended a
                // subscription that had lost its server as though it had finished.
                throw new InvalidOperationException($"The connection dropped: {exception.Message}", exception);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                // Only a normal closure is the server being done. Anything else (1011 for a server
                // error, the protocol's own 4400 and up) is the server saying why it stopped.
                if (result.CloseStatus is null or WebSocketCloseStatus.NormalClosure)
                {
                    return null;
                }

                throw new InvalidOperationException(
                    $"The server closed the connection: {(int) result.CloseStatus} {result.CloseStatusDescription}".TrimEnd());
            }

            builder.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(builder.GetBuffer(), 0, (int) builder.Length);
            }
        }
    }
}
