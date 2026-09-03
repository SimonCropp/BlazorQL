/// <summary>
/// Reading the errors a response carries. The path is the part with rules attached: indices are
/// data rather than document, and an error without one is informational.
/// </summary>
[TestFixture]
public class ResponseErrorsTests
{
    [Test]
    public void ReadsMessageAndPath()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"Error trying to resolve field 'accessGroup'.","path":["accessGroup"]}]}
            """);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0].Message, Is.EqualTo("Error trying to resolve field 'accessGroup'."));
        string[] expected = ["accessGroup"];
        Assert.That(errors[0].Path, Is.EqualTo(expected));
        Assert.That(errors[0].PathText, Is.EqualTo("accessGroup"));
        Assert.That(errors[0].HasPath, Is.True);
    }

    /// <summary>
    /// A list's selection set is written once however many elements come back, so an index
    /// identifies a datum and has no field to remove.
    /// </summary>
    [Test]
    public void DropsListIndicesFromThePath()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"boom","path":["accessGroups",0,"members",2,"id"]}]}
            """);

        string[] expected = ["accessGroups", "members", "id"];
        Assert.That(errors[0].Path, Is.EqualTo(expected));
        Assert.That(errors[0].PathText, Is.EqualTo("accessGroups.members.id"));
    }

    /// <summary>
    /// A validation failure never reached a field, and a scrubbed error list has had the path taken
    /// off it. Either way there is nothing to act on, and the pane says so by offering nothing.
    /// </summary>
    [Test]
    public void AnErrorWithNoPathIsNotActionable()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"Cannot query field 'nope' on type 'Query'."}]}
            """);

        Assert.That(errors, Has.Count.EqualTo(1));
        Assert.That(errors[0].HasPath, Is.False);
        Assert.That(errors[0].PathText, Is.Empty);
    }

    [Test]
    public void ReadsEveryError()
    {
        var errors = ResponseErrors.Parse(
            """
            {"errors":[{"message":"one","path":["a"]},{"message":"two","path":["b"]}],"data":{"a":null,"b":null}}
            """);

        string[] expected = ["a", "b"];
        Assert.That(errors.Select(_ => _.PathText), Is.EqualTo(expected));
    }

    [Test]
    public void IgnoresAResponseWithNoErrors()
    {
        Assert.That(ResponseErrors.Parse("""{"data":{"a":1}}"""), Is.Empty);
        Assert.That(ResponseErrors.Parse("""{"errors":[]}"""), Is.Empty);
    }

    /// <summary>The pane holds whatever came back, which is not always a graphql document.</summary>
    [Test]
    public void IgnoresWhatIsNotAResponse()
    {
        Assert.That(ResponseErrors.Parse(null), Is.Empty);
        Assert.That(ResponseErrors.Parse(""), Is.Empty);
        Assert.That(ResponseErrors.Parse("   "), Is.Empty);
        Assert.That(ResponseErrors.Parse("<html>502 Bad Gateway</html>"), Is.Empty);
        Assert.That(ResponseErrors.Parse("""{"errors":"not an array"}"""), Is.Empty);
        Assert.That(ResponseErrors.Parse("""{"errors":[{"path":["a"]}]}"""), Is.Empty);
    }
}
