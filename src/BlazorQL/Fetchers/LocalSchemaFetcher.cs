using System.Threading.Channels;

namespace BlazorQL;

/// <summary>
/// Executes against a schema that lives entirely in the browser: a JS module exporting
/// <c>createSchema(graphql)</c> (and optionally <c>createExecute(graphql)</c>), executed by the
/// page bundle's graphql-js. No server anywhere — which is what lets the sample deploy to static
/// hosting with subscriptions and incremental delivery intact.
/// </summary>
public sealed class LocalSchemaFetcher(string schemaModuleUrl) :
    IGraphQLFetcher
{
    JsModule? module;
    BlazorQLCallbacks? callbacks;
    bool initialized;
    int nextStream;

    /// <summary>Url of the schema module, resolved by the browser against the app's base.</summary>
    public string SchemaModuleUrl { get; } = schemaModuleUrl;

    internal void Attach(JsModule jsModule, BlazorQLCallbacks hub)
    {
        module = jsModule;
        callbacks = hub;
    }

    public async IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        if (module is null || callbacks is null)
        {
            throw new InvalidOperationException("The local schema fetcher is not attached to a BlazorQLIde.");
        }

        if (!initialized)
        {
            await module.Invoke("initLocalSchema", SchemaModuleUrl);
            initialized = true;
        }

        var streamId = $"local-{Interlocked.Increment(ref nextStream)}";
        var channel = Channel.CreateUnbounded<JsonElement>();

        void OnNext(string id, string json)
        {
            if (id == streamId)
            {
                channel.Writer.TryWrite(JsonDocument.Parse(json).RootElement);
            }
        }

        void OnComplete(string id)
        {
            if (id == streamId)
            {
                channel.Writer.TryComplete();
            }
        }

        void OnError(string id, string message)
        {
            if (id == streamId)
            {
                channel.Writer.TryComplete(new InvalidOperationException(message));
            }
        }

        callbacks.StreamNext += OnNext;
        callbacks.StreamComplete += OnComplete;
        callbacks.StreamError += OnError;
        try
        {
            await module.Invoke(
                "executeLocal",
                streamId,
                JsonSerializer.Serialize(new
                {
                    query = request.Query,
                    variables = request.Variables,
                    operationName = request.OperationName
                }));

            while (true)
            {
                JsonElement item;
                try
                {
                    if (!await channel.Reader.WaitToReadAsync(cancel))
                    {
                        break;
                    }

                    if (!channel.Reader.TryRead(out item))
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    await module.Invoke("stopLocalStream", streamId);
                    throw;
                }

                yield return item;
            }
        }
        finally
        {
            callbacks.StreamNext -= OnNext;
            callbacks.StreamComplete -= OnComplete;
            callbacks.StreamError -= OnError;
        }
    }
}
