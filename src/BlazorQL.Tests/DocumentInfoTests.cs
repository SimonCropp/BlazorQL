/// <summary>
/// The parser refuses a document nested past its depth limit — the guard that keeps a recursive
/// descent off the end of the stack — with an exception of its own rather than a syntax error.
/// Everything that reads a document asks <see cref="DocumentInfo"/> whether it parses, so that
/// refusal has to come back as an answer to the question rather than escape through it.
/// </summary>
public class DocumentInfoTests
{
    /// <summary>
    /// <paramref name="levels"/> fields, each selected inside the last. The parser counts depth in
    /// AST nodes against a limit of 128, and every level is two of them, the field and its
    /// selection set.
    /// </summary>
    public static string Nested(int levels) =>
        string.Concat(Enumerable.Repeat("{ a ", levels)) +
        string.Concat(Enumerable.Repeat("} ", levels));

    public static readonly string TooDeep = Nested(100);

    [Test]
    public async Task ADocumentNestedPastTheParsersLimitDoesNotParse()
    {
        var document = DocumentInfo.Parse(TooDeep);

        await Assert.That(document.Parses).IsFalse();
        await Assert.That(document.SyntaxError).IsNotNull();
        await Assert.That(document.Operations).IsEmpty();
    }

    /// <summary>The error lands where the nesting crossed the limit, for the editor to mark.</summary>
    [Test]
    public async Task TheErrorSaysWhereTheLimitWasCrossed()
    {
        var document = DocumentInfo.Parse(TooDeep);

        await Assert.That(document.SyntaxErrorLine).IsEqualTo(1);
        await Assert.That(document.SyntaxErrorColumn).IsGreaterThan(1);
        await Assert.That(document.SyntaxErrorColumn).IsLessThan(TooDeep.Length);
    }

    [Test]
    public async Task DeepNestingWithinTheLimitStillParses() =>
        await Assert.That(DocumentInfo.Parse(Nested(50)).Parses).IsTrue();

    /// <summary>Prettify has nothing to print, so the text stays as written.</summary>
    [Test]
    public async Task FormattingLeavesItAsWritten() =>
        await Assert.That(Formatter.FormatGraphQL(TooDeep)).IsEqualTo(TooDeep);

    [Test]
    public async Task MergingFragmentsReportsIt()
    {
        var (ok, text, error) = FragmentMerger.Merge(TooDeep);

        await Assert.That(ok).IsFalse();
        await Assert.That(text).IsNull();
        await Assert.That(error).IsNotNull();
    }
}
