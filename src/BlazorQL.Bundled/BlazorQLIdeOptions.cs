namespace BlazorQL;

/// <summary>
/// Configures the hosted IDE. Everything here is serialized into the page, so it is visible to
/// anyone who can reach the endpoint — never put a secret in <see cref="DefaultHeaders"/>.
/// </summary>
public sealed class BlazorQLIdeOptions
{
    /// <summary>
    /// Where the IDE posts queries and mutations. Root-relative (the default) or absolute. A ws://
    /// or wss:// url makes the whole session run over graphql-transport-ws.
    /// </summary>
    public string Endpoint { get; set; } = "/graphql";

    /// <summary>
    /// A separate graphql-transport-ws endpoint for subscriptions, the way GraphiQL pairs url with
    /// subscriptionUrl. Null runs everything through <see cref="Endpoint"/>.
    /// </summary>
    public string? SubscriptionEndpoint { get; set; }

    /// <summary>The query a fresh tab starts with.</summary>
    public string? DefaultQuery { get; set; }

    /// <summary>The request headers a fresh tab starts with, as a JSON object.</summary>
    public string? DefaultHeaders { get; set; }

    public bool IsHeadersEditorEnabled { get; set; } = true;

    /// <summary>
    /// Whether headers survive a reload. Off by default because headers usually hold credentials,
    /// and persisting them writes those to local storage.
    /// </summary>
    public bool ShouldPersistHeaders { get; set; }

    public int MaxHistoryLength { get; set; } = 20;

    /// <summary>Namespaces the IDE's local-storage keys. Change it to isolate two mounts.</summary>
    public string StorageNamespace { get; set; } = "blazorql";

    public IdeTheme DefaultTheme { get; set; } = IdeTheme.System;

    /// <summary>Pins the theme and hides the setting. Null lets the user choose.</summary>
    public IdeTheme? ForcedTheme { get; set; }

    /// <summary>The browser tab title.</summary>
    public string DocumentTitle { get; set; } = "GraphQL IDE";

    /// <summary>
    /// Overrides the base path baked into the page. Only needed behind a reverse proxy that strips
    /// a prefix without sending X-Forwarded-Prefix; prefer UseForwardedHeaders where you can.
    /// </summary>
    public string? BasePathOverride { get; set; }

    /// <summary>
    /// Serves the IDE for unknown extensionless paths under the mount rather than 404ing. Off by
    /// default: the IDE has no client-side routes, and share links travel in the fragment.
    /// </summary>
    public bool MapUnknownPathsToIde { get; set; }
}
