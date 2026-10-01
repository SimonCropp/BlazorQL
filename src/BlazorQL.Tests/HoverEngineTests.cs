/// <summary>
/// Hover docs, over the same <c>|</c> caret convention <see cref="ContextScannerTests"/> uses. The
/// caret marks a character inside the word being hovered, not a gap between tokens.
/// </summary>
public class HoverEngineTests
{
    readonly SchemaIndex fixture = ContextScannerTests.LoadFixture();
    readonly SchemaIndex roots = ContextScannerTests.Parse(ContextScannerTests.RootsSchema);

    string? Hover(string marked, SchemaIndex? schema = null)
    {
        var caret = marked.IndexOf('|');
        if (caret < 0)
        {
            throw new ArgumentException("the document needs a | caret marker", nameof(marked));
        }

        return HoverEngine.Hover(schema ?? fixture, marked.Remove(caret, 1), caret)?.Markdown;
    }

    [Test]
    public async Task AFieldShowsItsSignature() =>
        await Assert.That(Hover("{ per|son { name } }")).Contains("Query.person");

    [Test]
    public async Task AFieldArgumentShowsItsSignature() =>
        await Assert.That(Hover("""{ hasArgs(str|ing: "a") }""")).Contains("string: String");

    [Test]
    public async Task ATypeShowsItsKeyword() =>
        await Assert.That(Hover("{ ... on Per|son { name } }")).Contains("type Person");

    // The argument hovered inside a directive's parentheses is the directive's. Before this was
    // tracked, the enclosing field's argument of the same name answered instead.
    [Test]
    public async Task ADirectiveArgumentShowsTheDirectivesArgument() =>
        await Assert.That(Hover("{ pick @size(wid|th: 1) }", roots)).Contains("width: Int");

    [Test]
    public async Task AFieldArgumentNameReusedByADirectiveDoesNotAnswerForIt() =>
        await Assert.That(Hover("{ hasArgs @repeat(str|ing: 1) }")).IsNull();

    [Test]
    public async Task NothingIsSaidAboutAnUnknownWord() =>
        await Assert.That(Hover("{ no|pe }")).IsNull();
}