namespace BlazorQL.Bundled.Host;

using BlazorQL;
using Microsoft.JSInterop;

/// <summary>
/// The whole app: read the injected configuration, build a fetcher from it, render the IDE. There
/// is no router — the middleware serves one page — so nothing here depends on the mount path.
/// </summary>
public partial class Shell
{
    HostConfig config = new();
    IGraphQLFetcher fetcher = null!;

    /// <summary>
    /// Read synchronously. In WebAssembly <see cref="IJSRuntime"/> is always in-process, and doing
    /// it here rather than in Program.cs avoids a holder singleton: DI has to be configured before
    /// the host is built, which is before any <see cref="IJSRuntime"/> exists.
    /// </summary>
    protected override void OnInitialized()
    {
        config = ((IJSInProcessRuntime) JS).Invoke<HostConfig>("blazorqlHost.config");
        fetcher = CreateFetcher();
    }

    IGraphQLFetcher CreateFetcher()
    {
        var endpoint = Absolute(config.Endpoint);
        IGraphQLFetcher primary = IsWebSocket(endpoint) ? new GraphQLWsFetcher(endpoint) : new HttpFetcher(endpoint);

        if (config.SubscriptionEndpoint is not {Length: > 0} subscription)
        {
            return primary;
        }

        return new SplitFetcher(primary, new GraphQLWsFetcher(Absolute(subscription)));
    }

    static bool IsWebSocket(string url) =>
        url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a configured endpoint against the page it is being served from. Done in the browser
    /// rather than on the server so that reverse proxies, forwarded headers and the mount path never
    /// enter into it — and because HttpFetcher builds its own HttpClient with no BaseAddress, so a
    /// relative uri would throw.
    /// </summary>
    /// <remarks>
    /// The already-absolute test looks for a transport scheme rather than asking for UriKind.Absolute.
    /// The browser runtime is unix-like, and there a rooted path such as "/graphql" parses happily as
    /// an absolute file: uri — so the obvious version of this check hands the relative path straight
    /// back and the first request dies as net_http_client_invalid_requesturi.
    /// </remarks>
    string Absolute(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
            parsed.Scheme is "http" or "https" or "ws" or "wss")
        {
            return url;
        }

        var origin = ((IJSInProcessRuntime) JS).Invoke<string>("blazorqlHost.origin");
        return new Uri(new(origin), url).ToString();
    }

    static Theme? ParseTheme(string? value) =>
        Enum.TryParse<Theme>(value, ignoreCase: true, out var theme) ? theme : null;
}
