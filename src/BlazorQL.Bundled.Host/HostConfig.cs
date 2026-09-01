namespace BlazorQL.Bundled.Host;

/// <summary>
/// What the server side's <c>MapBlazorQL</c> options look like by the time they reach the browser.
/// The middleware injects this as <c>window.blazorqlConfig</c> when it renders index.html.
/// </summary>
/// <remarks>
/// A settable class rather than a positional record on purpose: the injected object may omit any
/// property, and the defaults below have to survive that. A record's constructor would bind the
/// missing ones to <c>default</c> instead.
/// </remarks>
public sealed class HostConfig
{
    /// <summary>Where queries and mutations are posted. Root-relative or absolute.</summary>
    public string Endpoint { get; set; } = "/graphql";

    /// <summary>
    /// Where subscriptions go, over graphql-transport-ws. Null runs subscriptions through
    /// <see cref="Endpoint"/>, which only works if it is itself a websocket url.
    /// </summary>
    public string? SubscriptionEndpoint { get; set; }

    public string? DefaultQuery { get; set; }

    public string? DefaultHeaders { get; set; }

    public bool IsHeadersEditorEnabled { get; set; } = true;

    public bool ShouldPersistHeaders { get; set; }

    public int MaxHistoryLength { get; set; } = 20;

    public string StorageNamespace { get; set; } = "blazorql";

    /// <summary>"System", "Light" or "Dark". Anything unrecognized falls back to System.</summary>
    public string? DefaultTheme { get; set; }

    /// <summary>Pins the theme and hides the setting. Null lets the user choose.</summary>
    public string? ForcedTheme { get; set; }
}
