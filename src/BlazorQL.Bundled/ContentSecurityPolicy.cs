namespace BlazorQL;

/// <summary>
/// The Content-Security-Policy the IDE needs, as directives. Every entry here is here because
/// something breaks without it, and the three unobvious ones all break silently - a blocked font, a
/// blocked worker, and a runtime that never compiles each leave a page that looks fine.
/// </summary>
/// <remarks>
/// Knowing this is the package's job, not the consuming app's. Set
/// <see cref="BlazorQLIdeOptions.WriteContentSecurityPolicy"/> and the mount sends it; call
/// <see cref="Build"/> or <see cref="Directives"/> to fold it into a policy the app writes itself.
/// </remarks>
public static class ContentSecurityPolicy
{
    /// <summary>
    /// The policy as a header value, ready for <c>Content-Security-Policy</c>.
    /// </summary>
    /// <param name="nonce">
    /// The nonce the page's script elements carry. Only for an app whose own policy names a nonce
    /// and no host source; the IDE has no inline script, so null - the usual case - is a complete
    /// policy on its own.
    /// </param>
    /// <param name="configure">Applied to the directives before they are joined.</param>
    public static string Build(string? nonce = null, Action<IDictionary<string, string>>? configure = null)
    {
        var directives = Directives(nonce);
        configure?.Invoke(directives);
        return string.Join("; ", directives.Select(_ => $"{_.Key} {_.Value}"));
    }

    /// <summary>
    /// The directives, for an app that composes its own policy. Mutable and keyed by directive
    /// name, so a caller can widen <c>connect-src</c> for a cross-origin endpoint or add its own
    /// hardening. Enumeration order is not meaningful - a policy is a set of directives.
    /// </summary>
    public static IDictionary<string, string> Directives(string? nonce = null) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["default-src"] = "'self'",
            // 'wasm-unsafe-eval' compiles the .NET runtime; without it the app never starts. 'self'
            // covers every script element - the page has no inline ones, and its configuration
            // travels in a data block the browser does not execute - including the ones Monaco's
            // AMD loader injects at runtime, which could not carry a nonce anyway.
            ["script-src"] = nonce is {Length: > 0} ? $"'self' 'nonce-{nonce}' 'wasm-unsafe-eval'" : "'self' 'wasm-unsafe-eval'",
            // Monaco writes its own styles.
            ["style-src"] = "'self' 'unsafe-inline'",
            ["img-src"] = "'self' data:",
            // Monaco's icon font is a data uri inside its stylesheet. Without this the toolbar
            // renders as empty boxes.
            ["font-src"] = "'self' data:",
            // Widen this when the graphql endpoint is not same-origin. It also governs the ws:// or
            // wss:// origin a subscription connects to.
            ["connect-src"] = "'self'",
            // Monaco starts its language workers from a blob url. Without this the editors still
            // work, but every keystroke logs a violation.
            ["worker-src"] = "'self' blob:"
        };
}
