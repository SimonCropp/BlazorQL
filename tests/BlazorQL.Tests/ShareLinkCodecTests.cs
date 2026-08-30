/// <summary>The share-link fragment codec: round-trips, unicode, and hostile input.</summary>
[TestFixture]
public class ShareLinkCodecTests
{
    [Test]
    public void RoundTrips()
    {
        var shared = new SharedQuery("query A { id }", """{"limit": 3}""");
        var fragment = ShareLinkCodec.Encode(shared);

        Assert.That(fragment, Does.StartWith("q="));
        Assert.That(ShareLinkCodec.TryDecode(fragment), Is.EqualTo(shared));
        // A leading # (as location.hash delivers it) decodes identically.
        Assert.That(ShareLinkCodec.TryDecode($"#{fragment}"), Is.EqualTo(shared));
    }

    [Test]
    public void RoundTripsUnicode()
    {
        var shared = new SharedQuery("query { greeting(name: \"héllo 你好 🚀\") }", """{"emoji": "😀"}""");
        Assert.That(ShareLinkCodec.TryDecode(ShareLinkCodec.Encode(shared)), Is.EqualTo(shared));
    }

    [Test]
    public void RoundTripsEmptyContent()
    {
        var shared = new SharedQuery("", "");
        Assert.That(ShareLinkCodec.TryDecode(ShareLinkCodec.Encode(shared)), Is.EqualTo(shared));
    }

    [Test]
    public void FragmentIsUrlSafe()
    {
        // Enough content to force + and / in plain base64; base64url must not contain either.
        var shared = new SharedQuery(new('?', 100), new('~', 100));
        var fragment = ShareLinkCodec.Encode(shared);

        // The payload after the q= prefix must be base64url: no +, /, or padding.
        Assert.That(fragment["q=".Length..], Does.Not.Contain("+").And.Not.Contain("/").And.Not.Contain("="));
        Assert.That(ShareLinkCodec.TryDecode(fragment), Is.EqualTo(shared));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("#")]
    [TestCase("#other=abc")]
    [TestCase("q=")]
    [TestCase("q=!!!not-base64!!!")]
    [TestCase("q=YWJj")]
    [TestCase("#q=eyJxdWVyeSI6IDF9")]
    public void MalformedDecodesToNull(string? hash) =>
        Assert.That(ShareLinkCodec.TryDecode(hash), Is.Null);

    [Test]
    public void PayloadMissingEitherMemberDecodesToNull()
    {
        // {"query":"x"} — no variables member.
        var queryOnly = Convert.ToBase64String("""{"query":"x"}"""u8.ToArray()).TrimEnd('=');
        Assert.That(ShareLinkCodec.TryDecode($"q={queryOnly}"), Is.Null);

        // {"variables":"x"} — no query member.
        var variablesOnly = Convert.ToBase64String("""{"variables":"x"}"""u8.ToArray()).TrimEnd('=');
        Assert.That(ShareLinkCodec.TryDecode($"q={variablesOnly}"), Is.Null);
    }

    [Test]
    public void HeadersCannotTravelByConstruction()
    {
        // The codec's only payload shape is SharedQuery: exactly a query and a variables text.
        // Headers have no slot — the API makes encoding them impossible rather than forbidden.
        var properties = typeof(SharedQuery).GetProperties();
        Assert.That(properties.Select(_ => _.Name), Is.EquivalentTo(["Query", "Variables"]));

        // And the encoded JSON carries only those two members.
        var fragment = ShareLinkCodec.Encode(new("q", "v"));
        var payload = fragment["q=".Length..].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        Assert.That(
            document.RootElement.EnumerateObject().Select(_ => _.Name),
            Is.EquivalentTo(["query", "variables"]));
    }
}
