[TestFixture]
public class SchemaIndexTests
{
    static string SchemaJson() =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "DocExplorerTests.schema.json"));

    [Test]
    public void ParsesAWrappedIntrospectionResult()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var schema = SchemaIndex.Parse(document.RootElement)!;

        Assert.That(schema, Is.Not.Null);
        Assert.That(schema.QueryTypeName, Is.EqualTo("Query"));
        Assert.That(schema.MutationTypeName, Is.Null);
        Assert.That(schema.Description, Does.Contain("hand-written"));
        Assert.That(schema.IsRootType("Query"), Is.True);
        Assert.That(schema.IsRootType("Person"), Is.False);

        var person = schema.Find("Person")!;
        Assert.That(person, Is.Not.Null);
        Assert.That(person.Kind, Is.EqualTo("OBJECT"));
        Assert.That(person.Interfaces!.Single().Name, Is.EqualTo("Named"));

        var query = schema.Find("Query")!;
        var hasArgs = query.Fields!.Single(_ => _.Name == "hasArgs");
        Assert.That(hasArgs.Args.Single(_ => _.Name == "count").DefaultValue, Is.EqualTo("0"));
        var deprecatedArg = hasArgs.Args.Single(_ => _.Name == "deprecatedArg");
        Assert.That(deprecatedArg.IsDeprecated, Is.True);
        Assert.That(deprecatedArg.DeprecationReason, Does.Contain("instead"));

        var friends = person.Fields!.Single(_ => _.Name == "friends");
        Assert.That(friends.Type.Display(), Is.EqualTo("[Person]"));
        Assert.That(friends.Type.Unwrap().Name, Is.EqualTo("Person"));

        var color = schema.Find("Color")!;
        Assert.That(color.EnumValues!.Single(_ => _.IsDeprecated).Name, Is.EqualTo("GRAY"));

        var petInput = schema.Find("PetInput")!;
        Assert.That(petInput.InputFields!.Single(_ => _.Name == "name").DefaultValue, Is.EqualTo("\"Rex\""));

        Assert.That(schema.Find("JSON")!.SpecifiedByURL, Is.EqualTo("https://example.com/json-spec"));

        var directive = schema.Directives.Single();
        Assert.That(directive.Name, Is.EqualTo("repeat"));
        Assert.That(directive.IsRepeatable, Is.True);
        Assert.That(directive.Locations.Single(), Is.EqualTo("FIELD"));
    }

    [Test]
    public void ParsesABareIntrospectionResult()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var bare = document.RootElement.GetProperty("data");
        var schema = SchemaIndex.Parse(bare)!;

        Assert.That(schema, Is.Not.Null);
        Assert.That(schema.QueryTypeName, Is.EqualTo("Query"));
    }

    [Test]
    public void ReturnsNullWhenTheShapeIsNotIntrospection()
    {
        using var document = JsonDocument.Parse("""{"data": {"something": 1}}""");
        Assert.That(SchemaIndex.Parse(document.RootElement), Is.Null);
    }
}
