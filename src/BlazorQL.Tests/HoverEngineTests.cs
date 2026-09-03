/// <summary>
/// Hover docs, over the same <c>|</c> caret convention <see cref="ContextScannerTests"/> uses. The
/// caret marks a character inside the word being hovered, not a gap between tokens.
/// </summary>
[TestFixture]
public class HoverEngineTests
{
    static readonly SchemaIndex fixture = ContextScannerTests.LoadFixture();
    static readonly SchemaIndex roots = ContextScannerTests.Parse(ContextScannerTests.RootsSchema);

    static string? Hover(string marked, SchemaIndex? schema = null)
    {
        var caret = marked.IndexOf('|');
        Assert.That(caret, Is.GreaterThanOrEqualTo(0), "the document needs a | caret marker");
        return HoverEngine.Hover(schema ?? fixture, marked.Remove(caret, 1), caret)?.Markdown;
    }

    [Test]
    public void AFieldShowsItsSignature() =>
        Assert.That(Hover("{ per|son { name } }"), Does.Contain("Query.person"));

    [Test]
    public void AFieldArgumentShowsItsSignature() =>
        Assert.That(Hover("""{ hasArgs(str|ing: "a") }"""), Does.Contain("string: String"));

    [Test]
    public void ATypeShowsItsKeyword() =>
        Assert.That(Hover("{ ... on Per|son { name } }"), Does.Contain("type Person"));

    // The argument hovered inside a directive's parentheses is the directive's. Before this was
    // tracked, the enclosing field's argument of the same name answered instead.
    [Test]
    public void ADirectiveArgumentShowsTheDirectivesArgument() =>
        Assert.That(Hover("{ pick @size(wid|th: 1) }", roots), Does.Contain("width: Int"));

    [Test]
    public void AFieldArgumentNameReusedByADirectiveDoesNotAnswerForIt() =>
        Assert.That(Hover("{ hasArgs @repeat(str|ing: 1) }"), Is.Null);

    [Test]
    public void NothingIsSaidAboutAnUnknownWord() =>
        Assert.That(Hover("{ no|pe }"), Is.Null);
}
