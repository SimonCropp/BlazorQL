using System.IO.Compression;
using System.Net;
using BlazorQL;

/// <summary>
/// The http contract of the mounted endpoints, without a browser: content negotiation, validators,
/// caching, and the shape of the rendered page.
/// </summary>
[TestFixture]
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
        new(new HttpClientHandler {AutomaticDecompression = DecompressionMethods.None});

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

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentEncoding, Does.Contain("br"));
        Assert.That(response.Headers.Vary, Does.Contain("Accept-Encoding"));
        Assert.That(response.Content.Headers.ContentLength, Is.EqualTo(bytes.Length));
        Assert.That(Decompress(bytes), Is.Not.Empty);
    }

    [Test]
    public async Task TheIdentityBytesAreTheDecodedBrotli()
    {
        using var client = Client();

        using var plain = await Get(client, bootScript, brotli: false);
        using var compressed = await Get(client, bootScript, brotli: true);
        var identity = await plain.Content.ReadAsByteArrayAsync();

        Assert.That(plain.Content.Headers.ContentEncoding, Is.Empty);
        Assert.That(identity, Is.EqualTo(Decompress(await compressed.Content.ReadAsByteArrayAsync())));
    }

    /// <summary>A zero quality is a refusal, which a Contains check would read as acceptance.</summary>
    [Test]
    public async Task HonoursAZeroQualityRefusalOfBrotli()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, IdeUrl + bootScript);
        request.Headers.AcceptEncoding.Add(new("br", 0));

        using var response = await client.SendAsync(request);

        Assert.That(response.Content.Headers.ContentEncoding, Is.Empty);
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

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotModified));
    }

    /// <summary>An etag identifies a representation, so the two codings cannot share one.</summary>
    [Test]
    public async Task UsesADistinctETagPerContentCoding()
    {
        using var client = Client();

        using var compressed = await Get(client, bootScript, brotli: true);
        using var identity = await Get(client, bootScript, brotli: false);

        Assert.That(compressed.Headers.ETag, Is.Not.EqualTo(identity.Headers.ETag));
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

        Assert.That(boot.Headers.CacheControl!.NoCache, Is.True);
        Assert.That(stable.Headers.CacheControl!.MaxAge, Is.EqualTo(TimeSpan.FromDays(365)));
    }

    [Test]
    public async Task AnUnknownAssetIs404NotHtml()
    {
        using var client = Client();

        using var response = await Get(client, "/_framework/does-not-exist.wasm", brotli: true);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task TheBareMountRedirectsToATrailingSlash()
    {
        using var handler = new HttpClientHandler {AllowAutoRedirect = false};
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(IdeUrl);

        Assert.That((int) response.StatusCode, Is.InRange(300, 399));
        Assert.That(response.Headers.Location!.ToString(), Does.EndWith("/graphql-ide/"));
    }

    [Test]
    public async Task TheIndexCarriesTheBaseHrefAndConfig()
    {
        using var client = Client();

        using var response = await Get(client, "/", brotli: false);
        var html = await response.Content.ReadAsStringAsync();

        Assert.That(html, Does.Contain("/graphql-ide/"));
        Assert.That(html, Does.Contain("window.blazorqlConfig"));
        Assert.That(response.Headers.CacheControl!.NoStore, Is.True);
    }

    /// <summary>
    /// A DefaultQuery holding a closing script tag must not be able to end the script element it is
    /// written into.
    /// </summary>
    [Test]
    public async Task TheConfigCannotBreakOutOfItsScriptElement()
    {
        using var client = Client();

        using var response = await Get(client, "/", brotli: false);
        var html = await response.Content.ReadAsStringAsync();
        var config = html[html.IndexOf("window.blazorqlConfig", StringComparison.Ordinal)..];
        var script = config[..config.IndexOf("</script>", StringComparison.Ordinal)];

        // The query survived, but only in escaped form.
        Assert.That(script, Does.Contain("alert(1)"));
        Assert.That(script, Does.Not.Contain("<script>"));
    }

    [Test]
    public async Task HeadReturnsHeadersAndNoBody()
    {
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Head, IdeUrl + bootScript);

        using var response = await client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentLength, Is.GreaterThan(0));
        Assert.That(await response.Content.ReadAsByteArrayAsync(), Is.Empty);
    }

    /// <summary>Reads a fingerprinted asset name out of the boot config the runtime itself uses.</summary>
    async Task<string> FindFingerprintedRoute(HttpClient client)
    {
        using var response = await Get(client, "/_framework/dotnet.js", brotli: true);
        var script = Encoding.UTF8.GetString(Decompress(await response.Content.ReadAsByteArrayAsync()));
        var match = Regex.Match(script, "\"(dotnet\\.native\\.[a-z0-9]{10}\\.wasm)\"");
        Assert.That(match.Success, "The boot config no longer names a fingerprinted native asset.");
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
