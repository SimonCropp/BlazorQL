using GraphQLParser;

/// <summary>
/// The validator's offset-to-position index has to put every diagnostic exactly where
/// GraphQLParser's own conversion would, line terminators and all, so it is checked against that
/// conversion at every offset.
/// </summary>
public class TextPositionsTests
{
    [Test]
    [Arguments("")]
    [Arguments("abc")]
    [Arguments("a\nb\nc")]
    [Arguments("a\r\nb\r\nc")]
    [Arguments("a\rb\rc")]
    [Arguments("\n\nxyz")]
    [Arguments("\r\n\r\n")]
    [Arguments("a\r\r\nb\n\rc\r")]
    [Arguments("{\n  user {\r\n    id\r  }\n}\n")]
    public async Task EveryOffsetMatchesTheParser(string text)
    {
        var positions = new TextPositions(text);

        for (var offset = 0; offset <= text.Length; offset++)
        {
            var expected = Location.FromLinearPosition(text, offset);
            await Assert.That(positions.LineColumn(offset)).IsEqualTo((expected.Line, expected.Column));
        }
    }

    /// <summary>Random mixes of the three terminators, for whatever the fixed cases miss.</summary>
    [Test]
    public async Task RandomTextsMatchTheParser()
    {
        var random = new Random(12345);
        string[] pieces = ["a", "bc", " ", "\n", "\r", "\r\n", "{", "}"];
        for (var sample = 0; sample < 200; sample++)
        {
            var text = string.Concat(Enumerable.Range(0, random.Next(0, 40)).Select(_ => pieces[random.Next(pieces.Length)]));
            var positions = new TextPositions(text);
            for (var offset = 0; offset <= text.Length; offset++)
            {
                var expected = Location.FromLinearPosition(text, offset);
                await Assert.That(positions.LineColumn(offset)).IsEqualTo((expected.Line, expected.Column));
            }
        }
    }

    /// <summary>A diagnostic after a lone carriage return lands where the parser says it does.</summary>
    [Test]
    public async Task ADiagnosticAfterACarriageReturnIsOnTheNextLine()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ProjectFiles.DocExplorerTests_schema_json)));
        var validator = new SchemaValidator(SchemaIndex.Parse(document.RootElement)!);

        var diagnostic = validator.Validate(DocumentInfo.Parse("{\r  nope\r}")).Single(_ => _.IsError);

        await Assert.That((diagnostic.Line, diagnostic.Column)).IsEqualTo((2, 3));
    }
}
