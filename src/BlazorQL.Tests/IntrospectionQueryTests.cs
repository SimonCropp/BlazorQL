/// <summary>
/// The introspection query has to work against a server that implements the spec as published,
/// not only one that has taken up the working drafts. GraphQL.NET is the common case for this
/// library and does neither by default: it gates input-value deprecation and repeatable directives
/// behind schema features, and has no specifiedByURL at all.
/// </summary>
public class IntrospectionQueryTests
{
    // The members later drafts added, all of which a conforming server may legitimately lack.
    static readonly string[] draftMembers =
    [
        "specifiedByURL",
        "isRepeatable",
        "args(includeDeprecated: true)",
        "inputFields(includeDeprecated: true)"
    ];

    [Test]
    public async Task TheFullQueryAsksForTheDraftAdditions()
    {
        var query = BlazorQLIde.IntrospectionQuery(draftAdditions: true);

        await Assert.That(draftMembers.Where(_ => !query.Contains(_))).IsEmpty();
        await Assert.That(query).Contains("__schema {\n    description");
    }

    /// <summary>
    /// A server rejects the whole document over one unknown field, so the fallback query has to
    /// carry none of them.
    /// </summary>
    [Test]
    public async Task ThePortableQueryAsksForNoneOfThem()
    {
        var query = BlazorQLIde.IntrospectionQuery(draftAdditions: false);

        await Assert.That(draftMembers.Where(query.Contains)).IsEmpty();
        // __InputValue is where the deprecation pair would sit, and it is the one the spec has
        // never had.
        var inputValue = query[query.IndexOf("fragment InputValue", StringComparison.Ordinal)..];
        await Assert.That(inputValue).DoesNotContain("isDeprecated");
    }

    /// <summary>What the drafts do not touch, and so must survive the fallback.</summary>
    [Test]
    public async Task ThePortableQueryKeepsWhatTheSpecAlwaysHad()
    {
        var query = BlazorQLIde.IntrospectionQuery(draftAdditions: false);

        await Assert.That(query).Contains("fields(includeDeprecated: true)");
        await Assert.That(query).Contains("enumValues(includeDeprecated: true)");
        await Assert.That(query).Contains("defaultValue");
        await Assert.That(query).Contains("possibleTypes");
    }
}