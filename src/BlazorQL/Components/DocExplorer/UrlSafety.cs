/// <summary>
/// Whether a url that came out of a schema may be put in an <c>href</c> or a <c>src</c>. Everything
/// the doc explorer renders is endpoint-controlled, and the bundled package serves the IDE on the
/// API's own origin, often with cookies — so a <c>javascript:</c> description link would run there.
/// </summary>
/// <remarks>
/// markdown-it, which GraphiQL renders descriptions with, refuses the same set through its
/// <c>validateLink</c> hook. Markdig has no equivalent switch, so the check lives here and the
/// callers apply it.
/// </remarks>
static class UrlSafety
{
    static readonly string[] renderableSchemes = ["http", "https", "mailto"];
    static readonly string[] webSchemes = ["http", "https"];

    /// <summary>
    /// A link or image target that may be rendered: relative and fragment targets, and absolute
    /// ones naming a scheme that cannot run code.
    /// </summary>
    public static bool IsRenderable(string? url) =>
        Scheme(url) is not {} scheme ||
        renderableSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An absolute http or https url, for the places that link out of the page rather than within
    /// it. A relative target is not one of those, so it does not pass here.
    /// </summary>
    public static bool IsWebLink([NotNullWhen(true)] string? url) =>
        Scheme(url) is {} scheme &&
        webSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The scheme a browser would read off the url, or null when it would read none. Whitespace and
    /// control characters come out first, because a browser strips them before it looks for the
    /// colon — <c>java&#9;script:alert(1)</c> is a javascript url.
    /// </summary>
    static string? Scheme(string? url)
    {
        if (url is null)
        {
            return null;
        }

        var cleaned = new string([.. url.Where(_ => !char.IsWhiteSpace(_) && !char.IsControl(_))]);
        var colon = cleaned.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        // A colon reached only after a path, query or fragment separator belongs to the path.
        if (cleaned.AsSpan(0, colon).IndexOfAny('/', '?', '#') >= 0)
        {
            return null;
        }

        return cleaned[..colon];
    }
}
