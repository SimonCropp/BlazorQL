using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;

/// <summary>
/// Which captured headers are worth replaying. A denylist rather than an allowlist, because the
/// headers worth keeping are app-specific — an authorization scheme, an x-* correlation id, a
/// client-version header — and cannot be enumerated, while the ones to drop are a fixed set the
/// browser owns.
/// </summary>
static class HeaderFilter
{
    static readonly HashSet<string> dropped = new(StringComparer.OrdinalIgnoreCase)
    {
        // Forbidden header names. The browser sets these itself and fetch ignores or rejects any
        // attempt to override them, so importing them is dead weight — and "cookie" is where a
        // captured session token lives, which has no business being persisted into localStorage.
        "accept-charset",
        "accept-encoding",
        "access-control-request-headers",
        "access-control-request-method",
        "connection",
        "content-length",
        "cookie",
        "cookie2",
        "date",
        "dnt",
        "expect",
        "host",
        "keep-alive",
        "origin",
        "referer",
        "te",
        "trailer",
        "transfer-encoding",
        "upgrade",
        "user-agent",
        "via",

        // Owned by the fetcher rather than by the tab. HttpFetcher only adds its negotiated Accept
        // when the user did not supply one, so importing the browser's
        // "application/json, text/plain, */*" would quietly disable multipart/mixed incremental
        // delivery on every imported request.
        "accept",
        "content-type",

        // Capture noise: real headers, but they describe the request that was recorded rather than
        // anything worth sending again.
        "accept-language",
        "cache-control",
        "pragma",
        "priority",
        "traceparent",
        "tracestate",
        "x-client-data",

        // HTTP/2 pseudo-headers, which devtools has historically listed with their colon stripped.
        // They are request metadata rather than headers and no client may set them.
        "authority",
        "method",
        "path",
        "scheme"
    };

    // Forbidden by prefix rather than by name: client hints and fetch metadata are all browser-set,
    // proxy headers are hop-by-hop, and a leading colon marks a pseudo-header.
    static readonly string[] droppedPrefixes = ["sec-", "proxy-", ":"];

    public static bool IsImportable(string name)
    {
        if (dropped.Contains(name))
        {
            return false;
        }

        foreach (var prefix in droppedPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The importable headers as the JSON object the headers editor holds, with the counts the
    /// status line reports. Written through Utf8JsonWriter rather than a serializer so nothing here
    /// is discovered by reflection in a trimmed build.
    /// </summary>
    public static (string Json, int Found, int Imported) ToJson(List<(string Name, string Value)> headers)
    {
        // Duplicates collapse the way HTTP joins a repeated field, in first-seen order. Without
        // this the count would promise more entries than the headers editor ends up showing.
        var kept = new List<(string Name, string Value)>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers.Where(_ => IsImportable(_.Name)))
        {
            if (seen.TryGetValue(name, out var at))
            {
                kept[at] = (kept[at].Name, $"{kept[at].Value}, {value}");
                continue;
            }

            seen.Add(name, kept.Count);
            kept.Add((name, value));
        }

        if (kept.Count == 0)
        {
            return ("", headers.Count, 0);
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Indented = true,
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                   }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in kept)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return (Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n", headers.Count, kept.Count);
    }
}
