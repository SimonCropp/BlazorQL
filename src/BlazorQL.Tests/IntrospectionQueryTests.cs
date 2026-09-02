/// <summary>
/// The introspection query has to work against a server that implements the spec as published,
/// not only one that has taken up the working drafts. GraphQL.NET is the common case for this
/// library and does neither by default: it gates input-value deprecation and repeatable directives
/// behind schema features, and has no specifiedByURL at all.
/// </summary>
[TestFixture]
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
    public void TheFullQueryAsksForTheDraftAdditions()
    {
        var query = BlazorQLIde.IntrospectionQuery(draftAdditions: true);

        Assert.That(draftMembers.Where(_ => !query.Contains(_)), Is.Empty);
        Assert.That(query, Does.Contain("__schema {\n    description"));
    }

    /// <summary>
    /// A server rejects the whole document over one unknown field, so the fallback query has to
    /// carry none of them.
    /// </summary>
    [Test]
    public void ThePortableQueryAsksForNoneOfThem()
    {
        var query = BlazorQLIde.IntrospectionQuery(draftAdditions: false);

        Assert.That(draftMembers.Where(query.Contains), Is.Empty);
        // __InputValue is where the deprecation pair would sit, and it is the one the spec has
        // never had.
        var inputValue = query[query.IndexOf("fragment InputValue", StringComparison.Ordinal)..];
        Assert.That(inputValue, Does.Not.Contain("isDeprecated"));
    }

    /// <summary>What the drafts do not touch, and so must survive the fallback.</summary>
    [Test]
    public void ThePortableQueryKeepsWhatTheSpecAlwaysHad()
    {
        var query = BlazorQLIde.IntrospectionQuery(draftAdditions: false);

        Assert.That(query, Does.Contain("fields(includeDeprecated: true)"));
        Assert.That(query, Does.Contain("enumValues(includeDeprecated: true)"));
        Assert.That(query, Does.Contain("defaultValue"));
        Assert.That(query, Does.Contain("possibleTypes"));
    }
}
