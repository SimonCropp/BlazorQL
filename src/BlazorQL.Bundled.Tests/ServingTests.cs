using System.IO.Compression;
/// <summary>
/// The http contract of the mounted endpoints, without a browser: content negotiation, validators,
/// caching, and the shape of the rendered page.
/// </summary>
public class ServingTests :
    BundledFixture
{
    /// <summary>Keeps its name across builds, so it is the one framework file that revalidates.</summary>
    const string bootScript = "/_framework/blazor.webassembly.js";

    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        // Round-trips through the injected config, and is the escaping case below.
        options.DefaultQuery = "{ id } </script><script>alert(1)</script>";
    }

    /// <summary>Decompression off, so a test sees exactly the bytes the endpoint wrote.</summary>
    static HttpClient Client() =>
        new(
            new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None
            });

    async Task<HttpResponseMessage> Get(HttpClient client, string path, bool brotli)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, IdeUrl + path);
        if (brotli)
        {
            request.Headers.AcceptEncoding.Add(new("br"));
        }

        return await client.SendAsync(request);
    }

    [Test]
    public async Task ServesBrotliWhenAccepted()
    {
        using var client = Client();

        using var response = await Get(client, bootScript, brotli: true);
        var bytes = await response.Content.ReadAsByteArrayAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentEncoding).Contains("br");
        await Assert.That(response.Headers.Vary).Contains("Accept-Encoding");
        await Assert.That(response.Content.Headers.ContentLength).IsEqualTo(bytes.Length);
        await Assert.That(Decompress(bytes)).IsNotEmpty();
    }

    [Test]
    public async Task TheIdentityBytesAreTheDecodedBrotli()
    {
        using var client = Client();

        using var plain = await Get(client, bootScript, brotli: false);
        using var compressed = await Get(client, bootScript, brotli: true);
        var identity = await plain.Content.ReadAsByteArrayAsync();

        await Assert.That(plain.Content.Headers.ContentEncoding).IsEmpty();
        await Assert.That(identity).IsEquivalentTo(Decompress(await compressed.Content.ReadAsByteArrayAsync()), CollectionOrdering.Matching);
    }

    /// <summary>A zero quality is a refusal, which a Contains check would read as acceptance.</summary>
    [Test]
    public async Task HonoursAZeroQualityRefusalOfBrotli()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, IdeUrl + bootScript);
        request.Headers.AcceptEncoding.Add(new("br", 0));

        using var response = await client.SendAsync(request);

        await Assert.That(response.Content.Headers.ContentEncoding).IsEmpty();
    }

    [Test]
    public async Task RevalidatesWithAnETag()
    {
        using var client = Client();

        using var seed = await Get(client, bootScript, brotli: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, IdeUrl + bootScript);
        request.Headers.AcceptEncoding.Add(new("br"));
        request.Headers.IfNoneMatch.Add(seed.Headers.ETag!);
        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
    }

    /// <summary>An etag identifies a representation, so the two codings cannot share one.</summary>
    [Test]
    public async Task UsesADistinctETagPerContentCoding()
    {
        using var client = Client();

        using var compressed = await Get(client, bootScript, brotli: true);
        using var identity = await Get(client, bootScript, brotli: false);

        await Assert.That(compressed.Headers.ETag)!.IsNotEqualTo(identity.Headers.ETag);
    }

    /// <summary>
    /// dotnet.js keeps its name across builds so it has to revalidate; everything else under
    /// _framework carries a fingerprint, so its url changes whenever its bytes do.
    /// </summary>
    [Test]
    public async Task CachesFingerprintedAssetsForeverAndTheRestNot()
    {
        using var client = Client();

        using var boot = await Get(client, "/_framework/dotnet.js", brotli: true);
        var fingerprinted = await FindFingerprintedRoute(client);
        using var stable = await Get(client, fingerprinted, brotli: true);

        await Assert.That(boot.Headers.CacheControl!.NoCache).IsTrue();
        await Assert.That(stable.Headers.CacheControl!.MaxAge).IsEqualTo(TimeSpan.FromDays(365));
    }

    [Test]
    public async Task AnUnknownAssetIs404NotHtml()
    {
        using var client = Client();

        using var response = await Get(client, "/_framework/does-not-exist.wasm", brotli: true);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheBareMountRedirectsToATrailingSlash()
    {
        using var handler = new HttpClientHandler {AllowAutoRedirect = false};
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(IdeUrl);

        await Assert.That((int) response.StatusCode).IsBetween(300, 399);
        await Assert.That(response.Headers.Location!.ToString()).EndsWith("/blazorql/");
    }

    [Test]
    public async Task TheIndexCarriesTheBaseHrefAndConfig()
    {
        using var client = Client();

        using var response = await Get(client, "/", brotli: false);
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(html).Contains("/blazorql/");
        await Assert.That(html).Contains("id=\"blazorql-config\"");
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
    }

    /// <summary>
    /// A DefaultQuery holding a closing script tag must not be able to end the data block it is
    /// written into. The html parser treats that block as raw text like any other script element,
    /// so this is the same escape the executable version needed.
    /// </summary>
    [Test]
    public async Task TheConfigCannotBreakOutOfItsScriptElement()
    {
        using var client = Client();

        using var response = await Get(client, "/", brotli: false);
        var html = await response.Content.ReadAsStringAsync();
        var config = html[html.IndexOf("id=\"blazorql-config\"", StringComparison.Ordinal)..];
        var script = config[..config.IndexOf("</script>", StringComparison.Ordinal)];

        // The query survived, but only in escaped form.
        await Assert.That(script).Contains("alert(1)");
        await Assert.That(script).DoesNotContain("<script>");
    }

    [Test]
    public async Task HeadReturnsHeadersAndNoBody()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Head, IdeUrl + bootScript);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentLength ?? 0).IsGreaterThan(0);
        await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEmpty();
    }

    /// <summary>Reads a fingerprinted asset name out of the boot config the runtime itself uses.</summary>
    async Task<string> FindFingerprintedRoute(HttpClient client)
    {
        using var response = await Get(client, "/_framework/dotnet.js", brotli: true);
        var script = Encoding.UTF8.GetString(Decompress(await response.Content.ReadAsByteArrayAsync()));
        var match = Regex.Match(script, "\"(dotnet\\.native\\.[a-z0-9]{10}\\.wasm)\"");
        await Assert.That(match.Success).IsTrue().Because("The boot config no longer names a fingerprinted native asset.");
        return "/_framework/" + match.Groups[1].Value;
    }

    static byte[] Decompress(byte[] bytes)
    {
        using var source = new MemoryStream(bytes);
        using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        brotli.CopyTo(buffer);
        return buffer.ToArray();
    }
}
