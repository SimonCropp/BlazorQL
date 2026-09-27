/// <summary>
/// Reading the errors a response carries. The path is the part with rules attached: indices are
/// data rather than document, and an error without one is informational.
/// </summary>
public class ResponseErrorsTests
{
    [Test]
    public async Task ReadsMessageAndPath()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"Error trying to resolve field 'accessGroup'.","path":["accessGroup"]}]}
            """);

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0].Message).IsEqualTo("Error trying to resolve field 'accessGroup'.");
        string[] expected = ["accessGroup"];
        await Assert.That(errors[0].Path).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(errors[0].PathText).IsEqualTo("accessGroup");
        await Assert.That(errors[0].HasPath).IsTrue();
    }

    /// <summary>
    /// A list's selection set is written once however many elements come back, so an index
    /// identifies a datum and has no field to remove.
    /// </summary>
    [Test]
    public async Task DropsListIndicesFromThePath()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"boom","path":["accessGroups",0,"members",2,"id"]}]}
            """);

        string[] expected = ["accessGroups", "members", "id"];
        await Assert.That(errors[0].Path).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(errors[0].PathText).IsEqualTo("accessGroups.members.id");
    }

    /// <summary>
    /// A validation failure never reached a field, and a scrubbed error list has had the path taken
    /// off it. Either way there is nothing to act on, and the pane says so by offering nothing.
    /// </summary>
    [Test]
    public async Task AnErrorWithNoPathIsNotActionable()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"Cannot query field 'nope' on type 'Query'."}]}
            """);

        await Assert.That(errors).Count().IsEqualTo(1);
        await Assert.That(errors[0].HasPath).IsFalse();
        await Assert.That(errors[0].PathText).IsEmpty();
    }

    [Test]
    public async Task ReadsEveryError()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"one","path":["a"]},{"message":"two","path":["b"]}],"data":{"a":null,"b":null}}
            """);

        string[] expected = ["a", "b"];
        await Assert.That(errors.Select(_ => _.PathText)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task IgnoresAResponseWithNoErrors()
    {
        await Assert.That(ResponseErrors.Parse("""{"data":{"a":1}}""")).IsEmpty();
        await Assert.That(ResponseErrors.Parse("""{"errors":[]}""")).IsEmpty();
    }

    /// <summary>The pane holds whatever came back, which is not always a graphql document.</summary>
    [Test]
    public async Task IgnoresWhatIsNotAResponse()
    {
        await Assert.That(ResponseErrors.Parse(null)).IsEmpty();
        await Assert.That(ResponseErrors.Parse("")).IsEmpty();
        await Assert.That(ResponseErrors.Parse("   ")).IsEmpty();
        await Assert.That(ResponseErrors.Parse("<html>502 Bad Gateway</html>")).IsEmpty();
        await Assert.That(ResponseErrors.Parse("""{"errors":"not an array"}""")).IsEmpty();
        await Assert.That(ResponseErrors.Parse("""{"errors":[{"path":["a"]}]}""")).IsEmpty();
    }
}