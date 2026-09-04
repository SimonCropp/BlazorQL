/// <summary>
/// Descriptions and deprecation reasons are endpoint-controlled, and the bundled package serves the
/// IDE on the API's own origin. So what markdown is allowed to point at matters as much as what
/// tags it is allowed to write.
/// </summary>
[TestFixture]
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
    [TestCase("javascript:alert(document.cookie)")]
    [TestCase("JavaScript:alert(1)")]
    [TestCase("vbscript:msgbox(1)")]
    [TestCase("data:text/html,alert(1)")]
    [TestCase("<java\tscript:alert(1)>")]
    [TestCase("<java script:alert(1)>")]
    [TestCase("<\u0001javascript:alert(1)>")]
    public void ALinkThatWouldRunCodeLosesItsTarget(string url)
    {
        var markup = Render($"[click me]({url})");

        Assert.That(markup, Does.Contain("click me"));
        Assert.That(Targets(markup), Is.Empty);
    }

    [Test]
    public void AnImageThatWouldRunCodeLosesItsTarget()
    {
        var markup = Render("![x](javascript:alert(1))");

        Assert.That(markup, Does.Contain("<img"));
        Assert.That(Targets(markup), Is.Empty);
    }

    [Test]
    public void AReferenceLinkIsCheckedToo()
    {
        var markup = Render(
            """
            [click me][ref]

            [ref]: javascript:alert(1)
            """);

        Assert.That(markup, Does.Contain("click me"));
        Assert.That(Targets(markup), Is.Empty);
    }

    [Test]
    public void APreviewIsCheckedToo()
    {
        var markup = Render("[click me](javascript:alert(1))", preview: true);

        Assert.That(markup, Does.Contain("click me"));
        Assert.That(Targets(markup), Is.Empty);
    }

    [TestCase("https://example.com/spec")]
    [TestCase("http://example.com")]
    [TestCase("mailto:someone@example.com")]
    [TestCase("../relative/page")]
    [TestCase("#anchor")]
    [TestCase("./weird:name")]
    public void AnOrdinaryTargetSurvives(string url)
    {
        var markup = Render($"[text]({url})");

        Assert.That(Targets(markup), Is.EqualTo([url]));
    }

    [Test]
    public void AnAutoLinkSurvives()
    {
        var markup = Render("See https://example.com for more.");

        Assert.That(Targets(markup), Is.EqualTo(autoLink));
    }

    static readonly string[] autoLink = ["https://example.com"];

    /// <summary>Raw html stays off; the target check is the second lock, not a replacement.</summary>
    [Test]
    public void RawHtmlIsStillNotRendered()
    {
        var markup = Render("<img src=x onerror=alert(1)>");

        Assert.That(markup, Does.Contain("&lt;img src=x onerror=alert(1)&gt;"));
        Assert.That(markup, Does.Not.Contain("<img"));
    }
}

/// <summary>
/// The <c>specifiedByURL</c> of a custom scalar goes straight into an href, and it comes from the
/// endpoint like every other description field.
/// </summary>
[TestFixture]
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

    [TestCase("javascript:alert(document.cookie)")]
    [TestCase("vbscript:msgbox(1)")]
    [TestCase("data:text/html,alert(1)")]
    [TestCase("/relative")]
    [TestCase("not a url")]
    [TestCase("")]
    [TestCase(null)]
    public void AUrlThatIsNotAWebLinkIsNotRenderedAsOne(string? url) =>
        Assert.That(Render(url), Does.Not.Contain("blazorql-doc-specified-by"));

    [TestCase("https://spec.example.com/scalars")]
    [TestCase("http://spec.example.com/scalars")]
    public void AWebLinkIsRendered(string url)
    {
        var markup = Render(url);

        Assert.That(markup, Does.Contain("blazorql-doc-specified-by"));
        Assert.That(markup, Does.Contain($"href=\"{url}\""));
    }
}
