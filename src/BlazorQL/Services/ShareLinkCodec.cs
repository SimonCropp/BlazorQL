namespace BlazorQL;

/// <summary>
/// What a share link carries: the operation text and the variables text. Headers are excluded by
/// construction — there is nowhere in this shape to put them.
/// </summary>
public sealed record SharedQuery(string Query, string Variables);

/// <summary>
/// Encodes a <see cref="SharedQuery"/> into a url fragment — <c>q=</c> followed by
/// base64url(UTF8(JSON)) — and decodes it back. Anything malformed decodes to null.
/// </summary>
public static class ShareLinkCodec
{
    const string fragmentPrefix = "q=";

    /// <summary>The fragment (no leading <c>#</c>) for the given content, e.g. <c>q=eyJ…</c>.</summary>
    public static string Encode(SharedQuery shared)
    {
        var json = JsonSerializer.Serialize(new
        {
            query = shared.Query,
            variables = shared.Variables
        });
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return fragmentPrefix + payload;
    }

    /// <summary>Decodes a location hash (leading <c>#</c> optional). Null for anything malformed.</summary>
    public static SharedQuery? TryDecode(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
        {
            return null;
        }

        var fragment = hash.StartsWith('#')
            ? hash[1..]
            : hash;
        if (!fragment.StartsWith(fragmentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var payload = fragment[fragmentPrefix.Length..]
            .Replace('-', '+')
            .Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("query", out var query) ||
                query.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("variables", out var variables) ||
                variables.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return new(query.GetString()!, variables.GetString()!);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
