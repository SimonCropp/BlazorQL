/// <summary>
/// Descriptions and deprecation reasons are endpoint-controlled, and the bundled package serves the
/// IDE on the API's own origin. So what markdown is allowed to point at matters as much as what
/// tags it is allowed to write.
/// </summary>
public class MarkdownViewTests
{
    static string Render(string content, bool preview = false)
    {
        using var context = new BunitContext();
        return context.Render<MarkdownView>(_ => _
                .Add(component => component.Content, content)
                .Add(component => component.Preview, preview))
            .Markup;
    }

    /// <summary>Every non-empty href and src the markup carries.</summary>
    static IReadOnlyList<string> Targets(string markup) =>
    [
        .. Regex.Matches(markup, "(?:href|src)=\"([^\"]*)\"")
            .Select(_ => _.Groups[1].Value)
            .Where(_ => _.Length > 0)
    ];

    // The angle-bracket forms are the ones that carry a space or a control character through the
    // parser, which is where a browser stripping them before it reads the scheme starts to matter.
    [Test]
    [Arguments("javascript:alert(document.cookie)")]
    [Arguments("JavaScript:alert(1)")]
    [Arguments("vbscript:msgbox(1)")]
    [Arguments("data:text/html,alert(1)")]
    [Arguments("<java\tscript:alert(1)>")]
    [Arguments("<java script:alert(1)>")]
    [Arguments("<\u0001javascript:alert(1)>")]
    public async Task ALinkThatWouldRunCodeLosesItsTarget(string url)
    {
        var markup = Render($"[click me]({url})");

        await Assert.That(markup).Contains("click me");
        await Assert.That(Targets(markup)).IsEmpty();
    }

    [Test]
    public async Task AnImageThatWouldRunCodeLosesItsTarget()
    {
        var markup = Render("![x](javascript:alert(1))");

        await Assert.That(markup).Contains("<img");
        await Assert.That(Targets(markup)).IsEmpty();
    }

    [Test]
    public async Task AReferenceLinkIsCheckedToo()
    {
        var markup = Render(
            """
            [click me][ref]

            [ref]: javascript:alert(1)
            """);

        await Assert.That(markup).Contains("click me");
        await Assert.That(Targets(markup)).IsEmpty();
    }

    [Test]
    public async Task APreviewIsCheckedToo()
    {
        var markup = Render("[click me](javascript:alert(1))", preview: true);

        await Assert.That(markup).Contains("click me");
        await Assert.That(Targets(markup)).IsEmpty();
    }

    [Test]
    [Arguments("https://example.com/spec")]
    [Arguments("http://example.com")]
    [Arguments("mailto:someone@example.com")]
    [Arguments("../relative/page")]
    [Arguments("#anchor")]
    [Arguments("./weird:name")]
    public async Task AnOrdinaryTargetSurvives(string url)
    {
        var markup = Render($"[text]({url})");

        await Assert.That(Targets(markup)).IsEquivalentTo([url], CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnAutoLinkSurvives()
    {
        var markup = Render("See https://example.com for more.");

        await Assert.That(Targets(markup)).IsEquivalentTo(autoLink, CollectionOrdering.Matching);
    }

    static readonly string[] autoLink = ["https://example.com"];

    /// <summary>Raw html stays off; the target check is the second lock, not a replacement.</summary>
    [Test]
    public async Task RawHtmlIsStillNotRendered()
    {
        var markup = Render("<img src=x onerror=alert(1)>");

        await Assert.That(markup).Contains("&lt;img src=x onerror=alert(1)&gt;");
        await Assert.That(markup).DoesNotContain("<img");
    }
}
/// <summary>
/// The <c>specifiedByURL</c> of a custom scalar goes straight into an href, and it comes from the
/// endpoint like every other description field.
/// </summary>
public class SpecifiedByLinkTests
{
    static string Render(string? url)
    {
        using var context = new BunitContext();
        return context.Render<TypeDoc>(_ => _
                .Add(
                    _ => _.Type,
                    new()
                    {
                        Kind = "SCALAR",
                        Name = "Url",
                        SpecifiedByURL = url
                    }))
            .Markup;
    }

    [Test]
    [Arguments("javascript:alert(document.cookie)")]
    [Arguments("vbscript:msgbox(1)")]
    [Arguments("data:text/html,alert(1)")]
    [Arguments("/relative")]
    [Arguments("not a url")]
    [Arguments("")]
    [Arguments(null)]
    public async Task AUrlThatIsNotAWebLinkIsNotRenderedAsOne(string? url) =>
        await Assert.That(Render(url)).DoesNotContain("blazorql-doc-specified-by");

    [Test]
    [Arguments("https://spec.example.com/scalars")]
    [Arguments("http://spec.example.com/scalars")]
    public async Task AWebLinkIsRendered(string url)
    {
        var markup = Render(url);

        await Assert.That(markup).Contains("blazorql-doc-specified-by");
        await Assert.That(markup).Contains($"href=\"{url}\"");
    }
}