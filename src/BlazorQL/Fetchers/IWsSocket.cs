/// <summary>
/// The socket surface <see cref="GraphQLWsProtocol"/> drives — a real websocket in production, a
/// scripted fake under test. One text message per call, in both directions.
/// </summary>
interface IWsSocket
{
    Task SendAsync(string json, Cancel cancel);

    /// <summary>The next complete text message, or null once the socket has closed.</summary>
    Task<string?> ReceiveAsync(Cancel cancel);
}
