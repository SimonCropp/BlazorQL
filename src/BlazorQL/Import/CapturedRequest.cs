/// <summary>
/// A network request as it was captured, before anything GraphQL-specific is read out of it. Each
/// of the copy formats reduces to this, so only one path turns a captured request into tabs.
/// </summary>
/// <param name="Url">
/// Kept only far enough to reach the query-string parameters a GET puts there. The importer
/// discards the endpoint itself: the IDE talks to the fetcher its host configured, not to whatever
/// host the request happened to be captured against.
/// </param>
/// <param name="Headers">Every header the capture carried, before any filtering.</param>
/// <param name="Body">The request body, or null for a GET.</param>
sealed record CapturedRequest(
    string? Url,
    List<(string Name, string Value)> Headers,
    string? Body)
{
    public static CapturedRequest None { get; } = new(null, [], null);

    /// <summary>
    /// Splits a captured header line. Devtools writes a valueless header as a bare name with a
    /// trailing semicolon, which is curl's "send this one empty" syntax rather than a malformation.
    /// </summary>
    public static (string Name, string Value)? ParseHeader(string text)
    {
        var colon = text.IndexOf(':');
        if (colon > 0)
        {
            // Split on the first colon only: a referer or an authorization value contains more.
            return (text[..colon].Trim(), text[(colon + 1)..].Trim());
        }

        var trimmed = text.Trim();
        if (trimmed.EndsWith(';') &&
            trimmed.Length > 1)
        {
            return (trimmed[..^1].Trim(), "");
        }

        return null;
    }
}
