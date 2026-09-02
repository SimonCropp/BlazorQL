/// <summary>
/// Serves one mount of the IDE. Everything except index.html is written straight out of the
/// assembly as brotli; index.html is rendered per base path and cached.
/// </summary>
sealed class IdeEndpoint(BlazorQLIdeOptions options, string prefix)
{
    /// <summary>
    /// Rendered pages, keyed by resolved base href. PathBase can legitimately vary per request
    /// behind a proxy, so this is a small map rather than a single value.
    /// </summary>
    ConcurrentDictionary<string, RenderedIndex> pages = new(StringComparer.Ordinal);

    /// <summary>
    /// The slot a nonce-carrying render leaves after every <c>&lt;script</c>, which
    /// <see cref="RenderedIndex.Resolve"/> fills with the request's nonce attribute. Chosen so that
    /// nothing a consumer can put in the page reaches the output as this text: every value
    /// substituted into index.html goes through either the html encoder or the json encoder, and
    /// both escape the angle brackets.
    /// </summary>
    const string noncePlaceholder = "<blazorql-nonce>";

    public async Task Handle(HttpContext context)
    {
        var path = context.Request.RouteValues["path"] as string ?? "";

        if (path.Length == 0)
        {
            await WriteIndex(context);
            return;
        }

        if (!IdeAssets.ByRoute.TryGetValue(path, out var asset))
        {
            if (options.MapUnknownPathsToIde &&
                !Path.HasExtension(path))
            {
                await WriteIndex(context);
                return;
            }

            // Deliberately not the ide: answering a .wasm request with html produces a mime error
            // that reads like a mystery instead of a missing file.
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await WriteAsset(context, asset);
    }

    static async Task WriteAsset(HttpContext context, IdeAsset asset)
    {
        var brotli = AcceptsBrotli(context.Request);
        var response = context.Response;
        var etag = brotli ? asset.ETag : asset.IdentityETag;

        response.Headers.Vary = HeaderNames.AcceptEncoding;
        response.Headers.CacheControl = asset.CacheControl;
        response.Headers.ETag = etag;
        response.ContentType = asset.ContentType;

        if (context.Request.Headers.IfNoneMatch.Contains(etag))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        if (brotli)
        {
            // Set before the first byte. The response compression middleware skips any response
            // that already carries a Content-Encoding, which is what keeps a consumer's global
            // UseResponseCompression from encoding these a second time.
            response.Headers.ContentEncoding = "br";
            response.ContentLength = asset.CompressedLength;
            if (HttpMethods.IsHead(context.Request.Method))
            {
                return;
            }

            await using var stream = asset.OpenCompressed();
            await stream.CopyToAsync(response.Body, context.RequestAborted);
            return;
        }

        var bytes = asset.Identity();
        response.ContentLength = bytes.Length;
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await response.Body.WriteAsync(bytes, context.RequestAborted);
    }

    public async Task WriteIndex(HttpContext context)
    {
        var page = pages
            .GetOrAdd(BaseHref(context), Render)
            .Resolve(options.Nonce?.Invoke(context));

        var response = context.Response;
        response.ContentType = "text/html; charset=utf-8";
        // The page carries the configuration, and the configuration is not part of the url.
        response.Headers.CacheControl = "no-store";
        response.ContentLength = page.Length;

        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await response.Body.WriteAsync(page, context.RequestAborted);
    }

    string BaseHref(HttpContext context)
    {
        if (options.BasePathOverride is {Length: > 0} over)
        {
            return over.EndsWith('/') ? over : over + '/';
        }

        var mount = context.Request.PathBase + new PathString(prefix);
        // ToUriComponent percent-encodes, so a mount with a space or a non-ascii segment still
        // produces a valid href.
        return mount.HasValue ? mount.ToUriComponent() + "/" : "/";
    }

    RenderedIndex Render(string baseHref)
    {
        var config = new ClientConfig(
            options.Endpoint,
            options.SubscriptionEndpoint,
            options.DefaultQuery,
            options.DefaultHeaders,
            options.IsHeadersEditorEnabled,
            options.ShouldPersistHeaders,
            options.MaxHistoryLength,
            options.StorageNamespace,
            options.DefaultTheme.ToString(),
            options.ForcedTheme?.ToString());

        // The default encoder escapes <, > and &, so a DefaultQuery containing "</script>" cannot
        // break out of the script element it is written into.
        var json = JsonSerializer.Serialize(config, IdeJson.Default.ClientConfig);

        var html = IdeAssets.IndexHtml
            .Replace(
                """<base href="/" />""",
                $"""<base href="{HtmlEncoder.Default.Encode(baseHref)}" /><script>window.blazorqlConfig = {json};</script>""",
                StringComparison.Ordinal)
            .Replace(
                "<title>GraphQL IDE</title>",
                $"<title>{HtmlEncoder.Default.Encode(options.DocumentTitle)}</title>",
                StringComparison.Ordinal);

        if (options.Nonce is null)
        {
            return new(html, carriesNonce: false);
        }

        // Every script element, not only the two inline ones. A policy that names a nonce and no
        // host source has to carry it on the src-based scripts too, and an ignored nonce on those
        // costs nothing. The closing tags start "</", so they are not matched.
        html = html.Replace("<script", $"<script{noncePlaceholder}", StringComparison.Ordinal);

        return new(html, carriesNonce: true);
    }

    /// <summary>
    /// One rendered index.html, cached per base path. A nonce is per request and so cannot be baked
    /// into that cache: a nonce-carrying render holds the placeholder instead, and pays a copy on
    /// the way out. Without one the bytes are final and every request writes the same array.
    /// </summary>
    sealed class RenderedIndex(string html, bool carriesNonce)
    {
        byte[]? rendered = carriesNonce ? null : Encoding.UTF8.GetBytes(html);

        public byte[] Resolve(string? nonce)
        {
            if (rendered is not null)
            {
                return rendered;
            }

            var attribute = nonce is {Length: > 0} ? $" nonce=\"{HtmlEncoder.Default.Encode(nonce)}\"" : "";
            return Encoding.UTF8.GetBytes(html.Replace(noncePlaceholder, attribute, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Parsed rather than substring-matched: "br;q=0" means the client explicitly refuses brotli,
    /// and a Contains("br") check would read it as acceptance.
    /// </summary>
    static bool AcceptsBrotli(HttpRequest request)
    {
        var header = request.Headers.AcceptEncoding;
        if (header.Count == 0)
        {
            return false;
        }

        if (!StringWithQualityHeaderValue.TryParseList(header, out var encodings))
        {
            return false;
        }

        var wildcard = false;
        foreach (var encoding in encodings)
        {
            var acceptable = encoding.Quality is not 0;
            if (encoding.Value.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                return acceptable;
            }

            if (encoding.Value.Equals("*", StringComparison.Ordinal))
            {
                wildcard = acceptable;
            }
        }

        return wildcard;
    }
}
