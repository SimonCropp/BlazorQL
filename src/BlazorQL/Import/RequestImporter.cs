namespace BlazorQL;

/// <summary>
/// Recovers GraphQL operations from a request copied out of a browser's network tab. Every shape
/// devtools' copy menu produces is accepted: a GET url, curl in either shell flavour, the PowerShell
/// <c>Invoke-WebRequest</c> form, a <c>fetch</c> snippet, and a bare JSON request body.
/// <para>
/// Nothing here throws. Malformed input comes back as a message meant to be shown to the person who
/// pasted it, in the same shape <see cref="FragmentMerger.Merge"/> uses.
/// </para>
/// </summary>
public static class RequestImporter
{
    const string unrecognised =
        "Could not recognise this. Paste a url, a curl command, a PowerShell command, a fetch snippet, or a JSON request body.";

    const string noQuery = "That looks like a request, but it carries no GraphQL query.";

    public static (bool Ok, IReadOnlyList<ImportedRequest> Requests, string? Error) Import(string pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return (false, [], "Nothing to import.");
        }

        var text = Preclean(pasted);
        if (text.Length == 0)
        {
            return (false, [], "Nothing to import.");
        }

        if (StartsWithWord(text, "curl"))
        {
            // Only the cmd flavour caret-escapes, and it always does — devtools wraps every
            // argument in one, so a single occurrence is enough to tell the two apart.
            var tokens = text.Contains("^\"", StringComparison.Ordinal)
                ? ShellTokenizer.TokenizeCmd(text)
                : ShellTokenizer.TokenizeBash(text);
            return FromCaptured(CurlReader.Read(tokens));
        }

        if (StartsWithWord(text, "invoke-webrequest") ||
            StartsWithWord(text, "invoke-restmethod") ||
            StartsWithWord(text, "iwr"))
        {
            return FromCaptured(PowerShellReader.Read(text));
        }

        if (text.Contains("fetch(", StringComparison.Ordinal))
        {
            return FromCaptured(FetchSnippetReader.Read(text));
        }

        if (text[0] is '{' or '[')
        {
            var (payloads, error) = RequestBodyReader.Read(text);
            return Build(payloads, error, []);
        }

        if (LooksLikeUrl(text))
        {
            var (payload, error) = RequestUrlReader.Read(text);
            return payload is null
                ? (false, [], error)
                : Build([payload], null, []);
        }

        return (false, [], unrecognised);
    }

    static (bool Ok, IReadOnlyList<ImportedRequest> Requests, string? Error) FromCaptured(CapturedRequest captured)
    {
        if (captured.Body is {Length: > 0} body)
        {
            var (payloads, error) = RequestBodyReader.Read(body);
            return Build(payloads, error, captured.Headers);
        }

        // No body means a GET, which carries the operation in its query string instead.
        if (captured.Url is {Length: > 0} url &&
            url.Contains('?'))
        {
            var (payload, error) = RequestUrlReader.Read(url);
            return payload is null
                ? (false, [], error)
                : Build([payload], null, captured.Headers);
        }

        return (false, [], noQuery);
    }

    static (bool Ok, IReadOnlyList<ImportedRequest> Requests, string? Error) Build(
        List<GraphQLPayload> payloads,
        string? error,
        List<(string Name, string Value)> headers)
    {
        if (payloads.Count == 0)
        {
            return (false, [], error ?? unrecognised);
        }

        // One captured request has one header set, however many operations it batched.
        var (json, found, imported) = HeaderFilter.ToJson(headers);
        var requests = new List<ImportedRequest>(payloads.Count);
        foreach (var payload in payloads)
        {
            var document = DocumentInfo.Parse(payload.Query);
            if (!document.Parses)
            {
                return (false, [], $"The query in the request is not valid GraphQL. {document.SyntaxError}");
            }

            requests.Add(new(
                Formatter.FormatGraphQL(payload.Query),
                payload.Variables is null
                    ? ""
                    : Formatter.FormatJson(payload.Variables),
                // Only a document with more than one operation needs its name pinned to the tab:
                // that is what scopes the variables check to the right declarations. A single
                // operation already names its own tab, and pinning would be redundant state.
                document.Operations.Count > 1
                    ? payload.OperationName
                    : null,
                json,
                found,
                imported));
        }

        return (true, requests, null);
    }

    /// <summary>
    /// Tolerances for where a request is likely to have been copied from before it got here: a chat
    /// message wraps it in a markdown fence, and a screenshot or a transcript of a terminal carries
    /// the prompt along with it. A non-breaking space is deliberately left alone — replacing it
    /// would silently alter a GraphQL string literal.
    /// </summary>
    static string Preclean(string pasted)
    {
        var text = pasted.Trim().TrimStart('\uFEFF').Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBreak = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstBreak > 0 &&
                lastFence > firstBreak)
            {
                text = text[(firstBreak + 1)..lastFence].Trim();
            }
        }

        foreach (var prompt in prompts)
        {
            if (text.StartsWith(prompt, StringComparison.Ordinal))
            {
                return text[prompt.Length..].TrimStart();
            }
        }

        // "PS C:\Users\me> curl ..." and anything else shaped like a PowerShell prompt.
        var arrow = text.IndexOf('>');
        if (text.StartsWith("PS ", StringComparison.Ordinal) &&
            arrow is > 0 and < 200)
        {
            return text[(arrow + 1)..].TrimStart();
        }

        return text;
    }

    static readonly string[] prompts = ["$ ", "> ", "# "];

    // A leading command word, allowing for "curl.exe".
    static bool StartsWithWord(string text, string word)
    {
        if (!text.StartsWith(word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length == word.Length)
        {
            return true;
        }

        var next = text[word.Length];
        return char.IsWhiteSpace(next) ||
               next == '.';
    }

    static bool LooksLikeUrl(string text)
    {
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A url copied without its scheme is still worth reading when it carries the parameters a
        // GraphQL GET puts in a query string.
        return text.Contains('?') &&
               (text.Contains("query=", StringComparison.Ordinal) ||
                text.Contains("variables=", StringComparison.Ordinal) ||
                text.Contains("operationName=", StringComparison.Ordinal) ||
                text.Contains("extensions=", StringComparison.Ordinal));
    }
}
