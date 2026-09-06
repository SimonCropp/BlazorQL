using System.Threading.Channels;

/// <summary>
/// A real GraphQL endpoint over the GraphiQL test schema, for the IDE to talk to. This is the half
/// of the loop the WebAssembly sample cannot provide: its schema runs in the browser, so nothing
/// there exercises browser to http to server and back.
/// </summary>
public static class SampleSchemaServer
{
    static BlazorQL.Sample.SampleSchema schema = CreateSchema();
    static DocumentExecuter executer = new();
    static GraphQLSerializer serializer = new();

    static BlazorQL.Sample.SampleSchema CreateSchema()
    {
        var created = new BlazorQL.Sample.SampleSchema();
        created.Initialize();
        return created;
    }

    /// <summary>
    /// <paramref name="authorize"/> gates the endpoint the way a real one would, so a test can ask
    /// what the IDE sends rather than only what it renders.
    /// </summary>
    public static void MapSampleSchema(
        this WebApplication app,
        string pattern = "/graphql",
        Func<HttpContext, bool>? authorize = null) =>
        app.MapPost(
            pattern,
            context =>
            {
                if (authorize is not null &&
                    !authorize(context))
                {
                    context.Response.StatusCode = 401;
                    return Task.CompletedTask;
                }

                return Handle(context);
            });

    static async Task Handle(HttpContext context)
    {
        var request = await JsonSerializer.DeserializeAsync<JsonElement>(context.Request.Body);
        var query = request.GetProperty("query").GetString() ?? "";

        var result = await executer.ExecuteAsync(
            _ =>
            {
                _.Schema = schema;
                _.Query = query;
                _.ThrowOnUnhandledException = false;
                if (request.TryGetProperty("operationName", out var operation) &&
                    operation.ValueKind == JsonValueKind.String)
                {
                    _.OperationName = operation.GetString();
                }

                if (request.TryGetProperty("variables", out var variables) &&
                    variables.ValueKind == JsonValueKind.Object)
                {
                    _.Variables = serializer.ReadNode<Inputs>(variables);
                }
            });

        // A subscription has no single document to answer with, so it takes the streaming type the
        // fetcher offered — which is how a real server comes to serve SSE without being asked to.
        if (result.Streams is {Count: > 0} streams &&
            context.Request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            await StreamEvents(context, streams.Values.First());
            return;
        }

        context.Response.ContentType = "application/json";
        await serializer.WriteAsync(context.Response.Body, result);
    }

    /// <summary>
    /// Serves one subscription as GraphQL over SSE, in the distinct-connections mode the fetcher
    /// speaks: a next event per result, then complete.
    /// </summary>
    static async Task StreamEvents(HttpContext context, IObservable<ExecutionResult> stream)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";

        var events = Channel.CreateUnbounded<ExecutionResult>();
        using var subscription = stream.Subscribe(new ChannelObserver(events.Writer));
        var cancel = context.RequestAborted;
        try
        {
            await foreach (var result in events.Reader.ReadAllAsync(cancel))
            {
                // Flushed one event at a time. Buffering them into one write would still leave a
                // valid stream, and would quietly cost the suite the only thing it is here to
                // prove: that the browser sees an event when it happens.
                await context.Response.WriteAsync($"event: next\ndata: {serializer.Serialize(result)}\n\n", cancel);
                await context.Response.Body.FlushAsync(cancel);
            }

            await context.Response.WriteAsync("event: complete\ndata:\n\n", cancel);
            await context.Response.Body.FlushAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            // The client stopped the subscription by closing the connection, which is how the
            // protocol says one ends.
        }
    }

    /// <summary>Hands the schema's observable to the response writer, one result at a time.</summary>
    sealed class ChannelObserver(ChannelWriter<ExecutionResult> writer) :
        IObserver<ExecutionResult>
    {
        public void OnNext(ExecutionResult value) =>
            writer.TryWrite(value);

        public void OnError(Exception error) =>
            writer.TryComplete(error);

        public void OnCompleted() =>
            writer.TryComplete();
    }
}
