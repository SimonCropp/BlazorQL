/// <summary>
/// The variables document checked against the operation's declarations. Every document here goes
/// through the checker whole, because what the pane shows is all of its answers or none: the
/// diagnostics pass swallows an exception, and the markers then stay as they were.
/// </summary>
public class VariablesCheckerTests
{
    readonly SchemaIndex schema = ContextScannerTests.LoadFixture();

    IReadOnlyList<string> Check(string query, string? variables)
    {
        var operation = DocumentInfo.Parse(query).OperationNode(null);
        if (operation is null)
        {
            throw new ArgumentException("the query has no operation", nameof(query));
        }

        if (variables is null)
        {
            return VariablesChecker.Check(schema, operation, null);
        }

        using var document = JsonDocument.Parse(variables);
        return VariablesChecker.Check(schema, operation, document.RootElement);
    }

    static readonly string[] wrongType = ["$term expects a String."];
    static readonly string[] notDeclared = ["Variable $nope is not declared by the operation."];
    static readonly string[] wrongIntType = ["$v expects an Int."];

    [Test]
    public async Task AValueOfTheWrongTypeIsReported() =>
        await Assert.That(Check("query Q($term: String) { search(term: $term) { __typename } }", """{"term": 1}""")).IsEquivalentTo(wrongType, CollectionOrdering.Matching);

    [Test]
    public async Task AVariableTheOperationDoesNotDeclareIsReported() =>
        await Assert.That(Check("query Q { person { name } }", """{"nope": 1}""")).IsEquivalentTo(notDeclared, CollectionOrdering.Matching);

    [Test]
    public async Task AMatchingDocumentIsAccepted() =>
        await Assert.That(Check("query Q($term: String) { search(term: $term) { __typename } }", """{"term": "a"}""")).IsEmpty();

    /// <summary>
    /// Two declarations of one name is a validator gap, not licence to throw: the exception was
    /// swallowed by the diagnostics pass, which left the variables pane showing whatever it had
    /// before, for as long as the duplicate was there.
    /// </summary>
    [Test]
    public async Task ADuplicateDeclarationTakesTheFirstRatherThanThrowing() =>
        await Assert.That(Check("query Q($v: Int, $v: Int) { person { name } }", """{"v": "not an int"}""")).IsEquivalentTo(wrongIntType, CollectionOrdering.Matching);

    [Test]
    public async Task ADuplicateDeclarationWithNoVariablesDocumentDoesNotThrow() =>
        await Assert.That(Check("query Q($v: Int, $v: Int) { person { name } }", null)).IsEmpty();
}