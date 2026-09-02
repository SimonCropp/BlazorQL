using System.Net;
using BlazorQL;

/// <summary>
/// The policy documented in docs/bundled.md, in a browser. Every directive there was added because
/// something broke without it, and a console assertion is the only thing that notices when one of
/// them stops being enough - a blocked font or worker is silent in the page itself.
/// </summary>
[TestFixture]
[Category("Browser")]
public class CspBundledIdeTests :
    BundledFixture
{
    protected override string ContentSecurityPolicy =>
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "worker-src 'self' blob:";

    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    [Test]
    public async Task BootsUnderTheDocumentedPolicy()
    {
        var page = await OpenIdeAsync();

        // Monaco reached the point of publishing its languages, which it cannot do if the boot
        // script was blocked or the runtime never compiled.
        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(_ => _.id)");

        Assert.That(languages, Does.Contain("graphql"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }
}

/// <summary>
/// The same policy with the nonce in place of 'unsafe-inline', which is the shape an app that
/// already runs a nonce-based policy wants.
/// </summary>
[TestFixture]
[Category("Browser")]
public class NoncedCspBundledIdeTests :
    BundledFixture
{
    protected override string ContentSecurityPolicy =>
        "default-src 'self'; " +
        "script-src 'self' 'nonce-{nonce}' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "worker-src 'self' blob:";

    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.Nonce = context => context.Items[NonceKey] as string;
    }

    [Test]
    public async Task BootsWithoutUnsafeInline()
    {
        var page = await OpenIdeAsync();

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(_ => _.id)");

        Assert.That(languages, Does.Contain("graphql"));
        Assert.That(ConsoleErrors(), Is.Empty);
    }
}

/// <summary>What the nonce option puts in the page, without a browser.</summary>
[TestFixture]
public class NonceTests :
    BundledFixture
{
    protected override string ContentSecurityPolicy => "script-src 'nonce-{nonce}'";

    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.Nonce = context => context.Items[NonceKey] as string;
        // The escaping case: a query holding the placeholder must not be substituted into.
        options.DefaultQuery = "{ id } <blazorql-nonce>";
    }

    static readonly Regex scriptTag = new("<script[^>]*>", RegexOptions.Compiled);

    async Task<(string Html, string Header)> GetIndex()
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(IdeUrl + "/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadAsStringAsync(),
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    /// <summary>
    /// Every script, not only the two inline ones: a policy naming a nonce and no host source has
    /// to carry it on the src-based scripts too.
    /// </summary>
    [Test]
    public async Task EveryScriptCarriesTheNonceFromTheHeader()
    {
        var (html, header) = await GetIndex();
        var nonce = Regex.Match(header, "'nonce-([A-F0-9]+)'").Groups[1].Value;
        var tags = scriptTag.Matches(html);

        Assert.That(nonce, Is.Not.Empty);
        Assert.That(tags, Is.Not.Empty);
        Assert.That(
            tags.Select(_ => _.Value).Where(_ => !_.Contains($"nonce=\"{nonce}\"")),
            Is.Empty);
    }

    /// <summary>The render is cached per base path; the nonce must not be cached with it.</summary>
    [Test]
    public async Task TheNonceChangesBetweenRequests()
    {
        var first = await GetIndex();
        var second = await GetIndex();

        Assert.That(first.Header, Is.Not.EqualTo(second.Header));
        Assert.That(first.Html, Is.Not.EqualTo(second.Html));
    }

    /// <summary>
    /// The placeholder only exists inside the cached render. Reaching a client would mean an
    /// invalid nonce on every script, which is a blank page.
    /// </summary>
    [Test]
    public async Task ThePlaceholderNeverReachesTheClient()
    {
        var (html, _) = await GetIndex();

        // Once, inside the serialized DefaultQuery, where the json encoder escaped the brackets.
        Assert.That(html, Does.Not.Contain("<blazorql-nonce>"));
        Assert.That(html, Does.Contain("blazorql-nonce"));
    }
}

/// <summary>A mount with no nonce provider, which is every mount that does not ask for one.</summary>
[TestFixture]
public class WithoutNonceTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options) =>
        options.Endpoint = "/graphql";

    [Test]
    public async Task ThePageCarriesNoNonceAttributes()
    {
        using var client = new HttpClient();

        var html = await client.GetStringAsync(IdeUrl + "/");

        Assert.That(html, Does.Not.Contain("nonce"));
    }
}
