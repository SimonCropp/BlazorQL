namespace BlazorQL.Sample;

/// <summary>
/// The query explorer page: the IDE over an endpoint the bar can point anywhere, defaulting to
/// the in-browser schema. No sidecar panel here — this page is the IDE itself.
/// </summary>
public partial class Explorer
{
    IGraphQLFetcher fetcher = null!;

    string endpoint = "";

    // The registered fetcher runs the built-in schema — the same instance the home page queries
    // through, so both pages land in the one sidecar log.
    protected override void OnInitialized() =>
        fetcher = SharedFetcher;

    // Empty falls back to the in-browser schema; ws(s):// speaks graphql-transport-ws; anything
    // else posts over http. Swapping the parameter makes the IDE re-introspect.
    void Apply()
    {
        var url = endpoint.Trim();
        IGraphQLFetcher transport;
        if (url.Length == 0)
        {
            transport = new LocalSchemaFetcher();
        }
        else if (url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
                 url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            transport = new GraphQLWsFetcher(url);
        }
        else
        {
            transport = new HttpFetcher(url);
        }

        fetcher = new SidecarFetcher(transport, Sidecar);
    }
}
