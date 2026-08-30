using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using GraphQL;
using GraphQL.SystemTextJson;

namespace BlazorQL.Sample;

/// <summary>
/// Executes against a schema that lives entirely in the browser: GraphQL.NET runs
/// <see cref="SampleSchema"/> inside the WASM app itself. No server (and no JS) anywhere — which
/// is what lets the sample deploy to static hosting with subscriptions intact. Incremental
/// delivery is the one loss: GraphQL.NET has no <c>@defer</c>/<c>@stream</c>, so those directives
/// fail validation like any unknown directive.
/// </summary>
public sealed partial class LocalSchemaFetcher :
    IGraphQLFetcher
{
    [GeneratedRegex(@"locations\s+args\(includeDeprecated: true\)")]
    private static partial Regex DirectiveArgsPattern();

    static readonly SampleSchema schema = new();
    static readonly DocumentExecuter executer = new();
    static readonly GraphQLSerializer serializer = new();

    public async IAsyncEnumerable<JsonElement> FetchAsync(
        GraphQLRequest request,
        IReadOnlyDictionary<string, string> headers,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        var query = request.Query;
        // Two GraphQL.NET introspection gaps, patched over in the IDE's introspection query only.
        // Dropping specifiedByURL costs nothing here (no type in this schema has a specifiedBy
        // url); dropping includeDeprecated on directive arguments costs nothing either (no
        // directive here has a deprecated argument). Field arguments keep it, so deprecatedArg
        // still shows up deprecated.
        if (query.Contains("query IntrospectionQuery"))
        {
            query = query.Replace("specifiedByURL", "");
            query = DirectiveArgsPattern().Replace(query, "locations args");
        }

        var result = await executer.ExecuteAsync(new()
        {
            Schema = schema,
            Query = query,
            Variables = request.Variables is { } variables
                ? serializer.ReadNode<Inputs>(variables)
                : null,
            OperationName = request.OperationName,
            CancellationToken = cancel,
            ThrowOnUnhandledException = false
        });

        if (result.Streams is not {Count: > 0} streams)
        {
            yield return Serialize(result);
            yield break;
        }

        // A subscription: one stream of events, each an ExecutionResult, adapted to the fetcher
        // shape through a channel. Cancellation disposes the subscription.
        var stream = streams.Values.Single();
        var channel = Channel.CreateUnbounded<ExecutionResult>();
        using (stream.Subscribe(new ChannelObserver(channel.Writer)))
        {
            while (await channel.Reader.WaitToReadAsync(cancel))
            {
                if (channel.Reader.TryRead(out var item))
                {
                    yield return Serialize(item);
                }
            }
        }
    }

    static JsonElement Serialize(ExecutionResult result)
    {
        using var document = JsonDocument.Parse(serializer.Serialize(result));
        return document.RootElement.Clone();
    }

    sealed class ChannelObserver(ChannelWriter<ExecutionResult> writer) :
        IObserver<ExecutionResult>
    {
        public void OnNext(ExecutionResult value) =>
            writer.TryWrite(value);

        public void OnCompleted() =>
            writer.TryComplete();

        public void OnError(Exception error) =>
            writer.TryComplete(error);
    }
}
