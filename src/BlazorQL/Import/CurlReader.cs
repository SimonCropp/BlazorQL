/// <summary>
/// The parts of a curl command worth keeping, read out of already-tokenized arguments.
/// </summary>
static class CurlReader
{
    /// <summary>
    /// Flags that consume the following argument. Their values are mostly ignored — the list exists
    /// so a flag's value can never be mistaken for the url, which is the one argument identified by
    /// position rather than by name.
    /// </summary>
    static readonly HashSet<string> valueFlags = new(StringComparer.Ordinal)
    {
        "-H", "--header",
        "-b", "--cookie",
        "-c", "--cookie-jar",
        "-d", "--data", "--data-raw", "--data-binary", "--data-ascii", "--data-urlencode", "--json",
        "-X", "--request",
        "--url",
        "-A", "--user-agent",
        "-e", "--referer",
        "-u", "--user",
        "-o", "--output",
        "-x", "--proxy",
        "-m", "--max-time",
        "--connect-timeout",
        "-w", "--write-out",
        "-F", "--form",
        "-T", "--upload-file",
        "-E", "--cert",
        "--key", "--cacert", "--capath",
        "--resolve", "--retry", "--interface", "--limit-rate",
        "-r", "--range",
        "--oauth2-bearer", "--aws-sigv4"
    };

    public static CapturedRequest Read(List<string> tokens)
    {
        var headers = new List<(string Name, string Value)>();
        string? url = null;
        string? body = null;

        var index = 0;
        if (tokens.Count > 0 &&
            IsCurl(tokens[0]))
        {
            index++;
        }

        while (index < tokens.Count)
        {
            var token = tokens[index];
            index++;
            if (token.Length < 2 ||
                token[0] != '-')
            {
                // The first bare argument is the url; later ones are curl's own extra urls, which
                // this feature has no use for.
                url ??= token;
                continue;
            }

            var name = token;
            string? value = null;
            var equals = token.IndexOf('=');
            if (token.StartsWith("--", StringComparison.Ordinal) &&
                equals > 0)
            {
                name = token[..equals];
                value = token[(equals + 1)..];
            }

            if (!valueFlags.Contains(name))
            {
                // An unknown or boolean flag carries nothing, so there is nothing to skip.
                continue;
            }

            if (value is null)
            {
                if (index >= tokens.Count)
                {
                    break;
                }

                value = tokens[index];
                index++;
            }

            switch (name)
            {
                case "-H" or "--header":
                    if (CapturedRequest.ParseHeader(value) is { } header)
                    {
                        headers.Add(header);
                    }

                    break;
                // Recorded rather than skipped so the "n of m headers" count is honest about what the
                // capture carried. The filter drops all three immediately afterwards.
                case "-b" or "--cookie":
                    headers.Add(("cookie", value));
                    break;
                case "-A" or "--user-agent":
                    headers.Add(("user-agent", value));
                    break;
                case "-e" or "--referer":
                    headers.Add(("referer", value));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii" or "--data-urlencode" or "--json":
                    // curl joins repeated data flags with an ampersand, so repeat that rather than
                    // letting a later flag silently win.
                    body = body is null
                        ? value
                        : $"{body}&{value}";
                    break;
                case "--url":
                    url = value;
                    break;
            }
        }

        return new(url, headers, body);
    }

    static bool IsCurl(string token) =>
        token.Equals("curl", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("curl.exe", StringComparison.OrdinalIgnoreCase);
}
