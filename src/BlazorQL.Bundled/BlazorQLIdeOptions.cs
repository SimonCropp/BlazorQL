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

    /// <summary>
    /// The CSP nonce for a request, stamped onto every script element in the page. Set it to serve
    /// the IDE under a policy that names a nonce instead of allowing <c>unsafe-inline</c>; null,
    /// the default, renders no nonce attributes at all.
    /// </summary>
    /// <remarks>
    /// The value has to be the one in that response's own Content-Security-Policy header, which
    /// this package never writes - the policy belongs to the consumer. Returning null or an empty
    /// string for a request renders that page without the attributes.
    /// </remarks>
    /// <example>
    /// <code>_.Nonce = context =&gt; (string?) context.Items["CspNonce"];</code>
    /// </example>
    public Func<HttpContext, string?>? Nonce { get; set; }

    /// <summary>
    /// Sends the Content-Security-Policy the IDE needs on the page this mount serves, with a
    /// per-request nonce that the page's scripts carry. Off by default, because a policy is the
    /// app's to decide - but knowing which directives the IDE needs is not, so this is the one line
    /// that gets them right.
    /// </summary>
    /// <remarks>
    /// The header is written on the page only, not on the assets, and only when the response does
    /// not already carry one - an app that sets its own policy for the mount keeps it. Setting
    /// <see cref="Nonce"/> as well hands the value over rather than generating one, for an app that
    /// mints the nonce itself.
    /// <para>
    /// See <see cref="BlazorQL.ContentSecurityPolicy"/> to fold the same directives into a policy
    /// the app writes on its own.
    /// </para>
    /// </remarks>
    public bool WriteContentSecurityPolicy { get; set; }

    /// <summary>
    /// Applied to the directives before <see cref="WriteContentSecurityPolicy"/> sends them, for
    /// the app's own hardening or to widen one the IDE leaves narrow. Ordered and mutable, so an
    /// entry can be added or replaced - appending a duplicate directive to the header would not
    /// override anything, because the first occurrence is the one that counts.
    /// </summary>
    /// <example>
    /// <code>
    /// _.ConfigureContentSecurityPolicy = _ =>
    /// {
    ///     _["connect-src"] = "'self' https://api.example.com";
    ///     _["frame-ancestors"] = "'none'";
    /// };
    /// </code>
    /// </example>
    public Action<IDictionary<string, string>>? ConfigureContentSecurityPolicy { get; set; }
}
