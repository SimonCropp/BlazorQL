/// <summary>
/// The client side of GraphQL over SSE in distinct-connections mode: the operation goes out as an
/// ordinary POST, and the answer comes back as an event stream rather than a document. A
/// <c>next</c> event carries an execution result — unwrapped, because a connection serving one
/// operation has nothing to demultiplex — and <c>complete</c> ends the stream. Comments and
/// unknown events are ignored, as the SSE spec asks.
/// </summary>
/// <remarks>
/// Stopping a subscription is closing the connection, which is what cancelling this enumeration
/// does: the caller disposes the response around it. There is no goodbye frame to send, so nothing
/// here mirrors the <c>complete</c> the websocket transport owes its server on the way out.
/// </remarks>
static class GraphQLSseProtocol
{
    /// <summary>The event type of an event that never named one, per the SSE spec.</summary>
    const string unnamed = "message";

    public static async IAsyncEnumerable<JsonElement> Run(
        Stream stream,
        [EnumeratorCancellation] Cancel cancel)
    {
        // The stream belongs to the response the caller is holding open, and is disposed with it.
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var name = unnamed;
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (line.Length == 0)
            {
                if (name == "complete")
                {
                    yield break;
                }

                // "next" is what the protocol sends. An event that named no type at all is the SSE
                // default, and a server that never names its events is answering with results all
                // the same — dropping those would leave a live subscription looking like a silent
                // one, which is the one failure here that gives no sign of itself.
                if (data.Length > 0 &&
                    name is "next" or unnamed)
                {
                    yield return Parse(data.ToString());
                }

                name = unnamed;
                data.Clear();
                continue;
            }

            // A line opening with a colon is a comment, which is how a server holds the connection
            // open between events.
            if (line[0] == ':')
            {
                continue;
            }

            var (field, value) = Split(line);
            switch (field)
            {
                case "event":
                    name = value;
                    break;
                case "data":
                    // Several data lines in one event are one value, joined by newlines.
                    if (data.Length > 0)
                    {
                        data.Append('\n');
                    }

                    data.Append(value);
                    break;
                default:
                    // "id" and "retry" mean nothing to an operation that is never resumed, and the
                    // spec asks for unknown fields to be ignored.
                    break;
            }
        }
    }

    /// <summary>
    /// Splits a field line at its first colon, dropping the one optional space that may follow it.
    /// A line with no colon at all is a field with an empty value, which the spec allows.
    /// </summary>
    static (string Field, string Value) Split(string line)
    {
        var colon = line.IndexOf(':');
        if (colon < 0)
        {
            return (line, "");
        }

        var value = line[(colon + 1)..];
        if (value.StartsWith(' '))
        {
            value = value[1..];
        }

        return (line[..colon], value);
    }

    /// <summary>
    /// Parses one event's data as a response document. Mirrors the http transport's own handling: a
    /// payload that is not JSON is a transport failure, reported with enough of itself to recognise.
    /// </summary>
    static JsonElement Parse(string data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            var preview = data[..Math.Min(data.Length, 500)];
            throw new InvalidOperationException($"The event stream carried a non-JSON event: {preview}");
        }
    }
}
