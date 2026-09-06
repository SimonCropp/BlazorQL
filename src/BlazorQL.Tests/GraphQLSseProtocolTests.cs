/// <summary>
/// The GraphQL over SSE reader: next events yield their execution result, complete ends the
/// stream, comment keep-alives and unknown events are skipped, multi-line data is rejoined, and a
/// non-JSON payload is a transport failure.
/// </summary>
[TestFixture]
public class GraphQLSseProtocolTests
{
    [Test]
    public async Task NextEventsThenComplete()
    {
        var results = await Collect(
            """
            event: next
            data: {"data":{"message":"Hi"}}

            event: next
            data: {"data":{"message":"Hola"}}

            event: complete
            data:


            """);

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
        Assert.That(results[1].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hola"));
    }

    [Test]
    public async Task CompleteEndsTheStreamBeforeWhateverFollows()
    {
        var results = await Collect(
            """
            event: next
            data: {"data":{"message":"Hi"}}

            event: complete
            data:

            event: next
            data: {"data":{"message":"after"}}


            """);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// A server holds an idle subscription open with comment lines, and may send events this client
    /// has no use for. Neither is a result.
    /// </summary>
    [Test]
    public async Task CommentsAndUnknownEventsAreSkipped()
    {
        var results = await Collect(
            """
            :

            :ping

            event: something-else
            data: {"data":{"message":"not a result"}}

            id: 7
            retry: 500
            event: next
            data: {"data":{"message":"Hi"}}


            """);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
    }

    /// <summary>
    /// A payload split across data lines is one value joined by newlines — which is how a server
    /// that pretty-prints its results sends them.
    /// </summary>
    [Test]
    public async Task MultiLineDataIsRejoined()
    {
        var results = await Collect(
            """
            event: next
            data: {"data":{
            data: "message":"Hi"
            data: }}


            """);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
    }

    /// <summary>
    /// The one optional space after the colon belongs to the framing, and every space after that
    /// belongs to the value.
    /// </summary>
    [Test]
    public async Task OnlyOneSpaceAfterTheColonIsFraming()
    {
        var results = await Collect(
            "event:next\n" +
            "data:{\"data\":{\"message\":\"Hi\"}}\n" +
            "\n" +
            "event: next\n" +
            "data:  {\"data\":{\"message\":\" padded\"}}\n" +
            "\n");

        Assert.That(results, Has.Count.EqualTo(2));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
        // The second space survived into the JSON, which tolerates leading whitespace.
        Assert.That(results[1].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo(" padded"));
    }

    /// <summary>
    /// An event that names no type is the SSE default, and a server that never names its events is
    /// still answering with results.
    /// </summary>
    [Test]
    public async Task AnUnnamedEventIsTakenAsAResult()
    {
        var results = await Collect(
            """
            data: {"data":{"message":"Hi"}}


            """);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
    }

    [Test]
    public async Task CarriageReturnsAreLineEndingsToo()
    {
        var results = await Collect("event: next\r\ndata: {\"data\":{\"message\":\"Hi\"}}\r\n\r\n");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
    }

    /// <summary>
    /// Errors raised before execution ride the stream as an ordinary result document rather than a
    /// failure status, which is what keeps the connection from dropping.
    /// </summary>
    [Test]
    public async Task PreExecutionErrorsArriveAsAResult()
    {
        var results = await Collect(
            """
            event: next
            data: {"errors":[{"message":"boom"}]}

            event: complete
            data:


            """);

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("errors")[0].GetProperty("message").GetString(), Is.EqualTo("boom"));
    }

    [Test]
    public void NonJsonDataThrows()
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            () => Collect(
                """
                event: next
                data: <html>gateway fell over</html>


                """));

        Assert.That(exception!.Message, Does.Contain("<html>gateway fell over"));
    }

    /// <summary>
    /// A stream that ends without a complete event ends the enumeration all the same — a server
    /// that closed on its own, rather than one that said goodbye.
    /// </summary>
    [Test]
    public async Task AStreamThatJustEndsYieldsWhatArrived()
    {
        var results = await Collect(
            """
            event: next
            data: {"data":{"message":"Hi"}}


            """);

        Assert.That(results, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// A trailing event with no blank line after it was never dispatched, and must not be: the
    /// blank line is what says the event is whole. The spec is explicit that a stream ending
    /// mid-event discards it.
    /// </summary>
    [Test]
    public async Task AnUnterminatedEventIsNotDispatched()
    {
        var results = await Collect(
            "event: next\n" +
            "data: {\"data\":{\"message\":\"Hi\"}}\n" +
            "\n" +
            "event: next\n" +
            "data: {\"data\":{\"message\":\"half arr");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].GetProperty("data").GetProperty("message").GetString(), Is.EqualTo("Hi"));
    }

    /// <summary>
    /// A fixture is a wire capture, so every event in one ends with its own blank line — including
    /// the last, which is why the raw strings here close on two. A blank line is what dispatches an
    /// event, and the newline that ends the final data line is not one.
    /// </summary>
    static async Task<List<JsonElement>> Collect(string stream, Cancel cancel = default)
    {
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(stream));
        List<JsonElement> results = [];
        await foreach (var element in GraphQLSseProtocol.Run(bytes, cancel))
        {
            results.Add(element);
        }

        return results;
    }
}
