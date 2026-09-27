/// <summary>
/// The policy documented in docs/bundled.md, in a browser. Every directive there was added because
/// something broke without it, and a console assertion is the only thing that notices when one of
/// them stops being enough - a blocked font or worker is silent in the page itself.
/// </summary>
[Category("Browser")]
public class CspBundledIdeTests :
    BundledFixture
{
    protected override string ContentSecurityPolicy =>
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
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

        await Assert.That(languages).Contains("graphql");
        await Assert.That(ConsoleErrors()).IsEmpty();
    }
}
/// <summary>
/// The same policy with a nonce added, which is the shape an app that already runs a nonce-based
/// policy wants. The IDE does not need it - nothing in the page is inline - so what this proves is
/// that stamping one onto every script element does not break the boot.
/// </summary>
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
    public async Task BootsUnderANoncePolicy()
    {
        var page = await OpenIdeAsync();

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(_ => _.id)");

        await Assert.That(languages).Contains("graphql");
        await Assert.That(ConsoleErrors()).IsEmpty();
    }
}
/// <summary>What the nonce option puts in the page, without a browser.</summary>
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

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadAsStringAsync(),
            response.Headers.GetValues("Content-Security-Policy").Single());
    }

    /// <summary>
    /// Every script element, none of which is inline: a policy naming a nonce and no host source
    /// has to carry it on the src-based scripts too.
    /// </summary>
    [Test]
    public async Task EveryScriptCarriesTheNonceFromTheHeader()
    {
        var (html, header) = await GetIndex();
        var nonce = Regex.Match(header, "'nonce-([A-F0-9]+)'").Groups[1].Value;
        var tags = scriptTag.Matches(html);

        await Assert.That(nonce).IsNotEmpty();
        await Assert.That(tags).IsNotEmpty();
        await Assert.That(tags.Select(_ => _.Value).Where(_ => !_.Contains($"nonce=\"{nonce}\""))).IsEmpty();
    }

    /// <summary>The render is cached per base path; the nonce must not be cached with it.</summary>
    [Test]
    public async Task TheNonceChangesBetweenRequests()
    {
        var first = await GetIndex();
        var second = await GetIndex();

        await Assert.That(first.Header).IsNotEqualTo(second.Header);
        await Assert.That(first.Html).IsNotEqualTo(second.Html);
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
        await Assert.That(html).DoesNotContain("<blazorql-nonce>");
        await Assert.That(html).Contains("blazorql-nonce");
    }
}
/// <summary>A mount with no nonce provider, which is every mount that does not ask for one.</summary>
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

        await Assert.That(html).DoesNotContain("nonce");
    }
}
/// <summary>
/// The one-line path: the mount sends the policy itself. Knowing which directives the IDE needs is
/// the package's job, and this is the fixture that proves the set it ships with is complete.
/// </summary>
[Category("Browser")]
public class WrittenCspBundledIdeTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.WriteContentSecurityPolicy = true;
    }

    [Test]
    public async Task BootsOnTheOptionAlone()
    {
        var page = await OpenIdeAsync();

        var languages = await page.EvaluateAsync<string[]>(
            "() => monaco.languages.getLanguages().map(_ => _.id)");

        await Assert.That(languages).Contains("graphql");
        await Assert.That(ConsoleErrors()).IsEmpty();
    }
}
/// <summary>The header the option writes, without a browser.</summary>
public class WrittenCspTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.WriteContentSecurityPolicy = true;
        options.ConfigureContentSecurityPolicy = _ =>
        {
            // Replacing one the IDE leaves narrow, and adding one of the app's own.
            _["connect-src"] = "'self' https://api.example.com";
            _["frame-ancestors"] = "'none'";
        };
    }

    async Task<HttpResponseMessage> GetIndex()
    {
        using var client = new HttpClient();
        return await client.GetAsync(IdeUrl + "/");
    }

    [Test]
    public async Task ThePolicyCarriesWhatTheIdeNeeds()
    {
        using var response = await GetIndex();
        var csp = response.Headers.GetValues("Content-Security-Policy")
            .Single();

        await Assert.That(csp).Contains("'wasm-unsafe-eval'");
        await Assert.That(csp).Contains("font-src 'self' data:");
        await Assert.That(csp).Contains("worker-src 'self' blob:");
        await Assert.That(csp).Contains("style-src 'self' 'unsafe-inline'");
    }

    /// <summary>
    /// Nothing in the page is inline, so there is no nonce to mint and nothing to keep in step: the
    /// header is the same bytes on every request, and the page carries no attributes at all.
    /// </summary>
    [Test]
    public async Task ThePolicyNeedsNoNonce()
    {
        using var first = await GetIndex();
        using var second = await GetIndex();
        var csp = first.Headers.GetValues("Content-Security-Policy")
            .Single();
        var html = await first.Content.ReadAsStringAsync();

        await Assert.That(csp).Contains("script-src 'self' 'wasm-unsafe-eval'");
        await Assert.That(csp).DoesNotContain("nonce-");
        await Assert.That(html).DoesNotContain("nonce");
        await Assert.That(second.Headers.GetValues("Content-Security-Policy").Single()).IsEqualTo(csp);
    }

    [Test]
    public async Task ConfigureReplacesAndAdds()
    {
        using var response = await GetIndex();
        var csp = response.Headers.GetValues("Content-Security-Policy")
            .Single();

        await Assert.That(csp).Contains("connect-src 'self' https://api.example.com");
        await Assert.That(csp).Contains("frame-ancestors 'none'");
        // Replaced, not appended - a duplicate directive would be ignored by the browser.
        await Assert.That(Regex.Matches(csp, "connect-src")).Count().IsEqualTo(1);
    }

    /// <summary>Assets are not documents; a policy on them restricts nothing.</summary>
    [Test]
    public async Task TheAssetsCarryNoPolicy()
    {
        using var client = new HttpClient();

        using var response = await client.GetAsync(IdeUrl + "/_framework/blazor.webassembly.js");

        await Assert.That(response.Headers.Contains("Content-Security-Policy")).IsFalse();
    }
}
/// <summary>
/// The option and the nonce provider together, for an app that mints one for every response and
/// wants the mount's policy to name it.
/// </summary>
public class WrittenCspWithNonceTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.WriteContentSecurityPolicy = true;
        // Constant rather than per request: what is under test is that the two sides agree, and a
        // fixed value makes the disagreement readable when they do not.
        options.Nonce = _ => "DEADBEEF";
    }

    [Test]
    public async Task ThePolicyNamesTheNonceThePageCarries()
    {
        using var client = new HttpClient();

        using var response = await client.GetAsync(IdeUrl + "/");
        var csp = response.Headers.GetValues("Content-Security-Policy")
            .Single();
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(csp).Contains("'nonce-DEADBEEF'");
        var scripts = Regex.Matches(html, "<script[^>]*>");
        await Assert.That(scripts).IsNotEmpty();
        await Assert.That(scripts.Select(_ => _.Value).Where(_ => !_.Contains("nonce=\"DEADBEEF\""))).IsEmpty();
    }
}
/// <summary>
/// The property the whole policy rests on: the page has no executable inline script. The bootstrap
/// is a file and the configuration is a data block of a type no browser executes, which is why
/// <c>script-src 'self'</c> runs the IDE with neither 'unsafe-inline' nor a nonce. A stray inline
/// block would boot fine here and break every consumer running a policy.
/// </summary>
public class NoInlineScriptTests :
    BundledFixture
{
    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.DefaultQuery = "{ id }";
    }

    [Test]
    public async Task EveryScriptElementIsAFileOrADataBlock()
    {
        using var client = new HttpClient();

        var html = await client.GetStringAsync(IdeUrl + "/");
        var tags = Regex.Matches(html, "<script[^>]*>")
            .Select(_ => _.Value);

        await Assert.That(tags.Where(_ => !_.Contains("src=") && !_.Contains("""type="application/json"""))).IsEmpty();
    }

    /// <summary>The data block is still where the configuration travels, and it is still read.</summary>
    [Test]
    public async Task TheConfigurationIsInTheDataBlock()
    {
        using var client = new HttpClient();

        var html = await client.GetStringAsync(IdeUrl + "/");

        await Assert.That(html).Contains("""<script type="application/json" id="blazorql-config">""");
        await Assert.That(html).Contains("{ id }");
    }
}
/// <summary>
/// An app-wide policy assigned by middleware before the mount runs - the shape of most
/// security-header middleware - is replaced on the page, not left to block the IDE's scripts.
/// </summary>
public class WrittenCspReplacesTheAppsTests :
    BundledFixture
{
    protected override string ContentSecurityPolicy => "script-src 'strict-dynamic' 'nonce-{nonce}'";

    protected override void Configure(BlazorQLIdeOptions options)
    {
        options.Endpoint = "/graphql";
        options.WriteContentSecurityPolicy = true;
    }

    [Test]
    public async Task TheMountsPolicyReplacesIt()
    {
        using var client = new HttpClient();

        using var response = await client.GetAsync(IdeUrl + "/");
        var csp = response.Headers.GetValues("Content-Security-Policy")
            .Single();

        await Assert.That(csp).IsEqualTo(BlazorQL.ContentSecurityPolicy.Build());
    }
}
/// <summary>
/// The overload that takes only the configuration, for an app that has something to configure but
/// no reason to move the mount off the default.
/// </summary>
public class DefaultMountTests :
    BundledFixture
{
    protected override bool MountAtDefault => true;

    /// <summary>Where the overload put it, which is what the test is checking.</summary>
    protected override string Mount => BlazorQLIdeEndpointRouteBuilderExtensions.DefaultPattern;

    protected override void Configure(BlazorQLIdeOptions options) =>
        options.DocumentTitle = "Configured";

    [Test]
    public async Task TheDefaultPatternIsWhereItMounts()
    {
        using var client = new HttpClient();

        var html = await client.GetStringAsync(IdeUrl + "/");

        await Assert.That(BlazorQLIdeEndpointRouteBuilderExtensions.DefaultPattern).IsEqualTo("/blazorql");
        await Assert.That(html).Contains("<title>Configured</title>");
    }
}