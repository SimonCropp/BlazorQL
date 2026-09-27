/// <summary>The share-link fragment codec: round-trips, unicode, and hostile input.</summary>
public class ShareLinkCodecTests
{
    [Test]
    public async Task RoundTrips()
    {
        var shared = new SharedQuery("query A { id }", """{"limit": 3}""");
        var fragment = ShareLinkCodec.Encode(shared);

        await Assert.That(fragment).StartsWith("q=");
        await Assert.That(ShareLinkCodec.TryDecode(fragment)).IsEqualTo(shared);
        // A leading # (as location.hash delivers it) decodes identically.
        await Assert.That(ShareLinkCodec.TryDecode($"#{fragment}")).IsEqualTo(shared);
    }

    [Test]
    public async Task RoundTripsUnicode()
    {
        var shared = new SharedQuery("query { greeting(name: \"héllo 你好 🚀\") }", """{"emoji": "😀"}""");
        await Assert.That(ShareLinkCodec.TryDecode(ShareLinkCodec.Encode(shared))).IsEqualTo(shared);
    }

    [Test]
    public async Task RoundTripsEmptyContent()
    {
        var shared = new SharedQuery("", "");
        await Assert.That(ShareLinkCodec.TryDecode(ShareLinkCodec.Encode(shared))).IsEqualTo(shared);
    }

    [Test]
    public async Task FragmentIsUrlSafe()
    {
        // Enough content to force + and / in plain base64; base64url must not contain either.
        var shared = new SharedQuery(new('?', 100), new('~', 100));
        var fragment = ShareLinkCodec.Encode(shared);

        // The payload after the q= prefix must be base64url: no +, /, or padding.
        await Assert.That(fragment["q=".Length..]).DoesNotContain("+").And.DoesNotContain("/").And.DoesNotContain("=");
        await Assert.That(ShareLinkCodec.TryDecode(fragment)).IsEqualTo(shared);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("#")]
    [Arguments("#other=abc")]
    [Arguments("q=")]
    [Arguments("q=!!!not-base64!!!")]
    [Arguments("q=YWJj")]
    [Arguments("#q=eyJxdWVyeSI6IDF9")]
    public async Task MalformedDecodesToNull(string? hash) =>
        await Assert.That(ShareLinkCodec.TryDecode(hash)).IsNull();

    [Test]
    public async Task PayloadMissingEitherMemberDecodesToNull()
    {
        // {"query":"x"} — no variables member.
        var queryOnly = Convert.ToBase64String("""{"query":"x"}"""u8.ToArray()).TrimEnd('=');
        await Assert.That(ShareLinkCodec.TryDecode($"q={queryOnly}")).IsNull();

        // {"variables":"x"} — no query member.
        var variablesOnly = Convert.ToBase64String("""{"variables":"x"}"""u8.ToArray()).TrimEnd('=');
        await Assert.That(ShareLinkCodec.TryDecode($"q={variablesOnly}")).IsNull();
    }

    [Test]
    public async Task HeadersCannotTravelByConstruction()
    {
        // The codec's only payload shape is SharedQuery: exactly a query and a variables text.
        // Headers have no slot — the API makes encoding them impossible rather than forbidden.
        var properties = typeof(SharedQuery).GetProperties();
        await Assert.That(properties.Select(_ => _.Name)).IsEquivalentTo(["Query", "Variables"]);

        // And the encoded JSON carries only those two members.
        var fragment = ShareLinkCodec.Encode(new("q", "v"));
        var payload = fragment["q=".Length..].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        await Assert.That(document.RootElement.EnumerateObject().Select(_ => _.Name))
            .IsEquivalentTo(["query", "variables"]);
    }
}