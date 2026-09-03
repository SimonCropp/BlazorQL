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

    /// <summary>
    /// The member lookups the language layer resolves names through. Built lazily per type, so the
    /// first ask and every one after it have to agree.
    /// </summary>
    [Test]
    public void MemberLookupsFindWhatAScanWouldHave()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var schema = SchemaIndex.Parse(document.RootElement)!;

        var query = schema.Find("Query")!;
        Assert.That(schema.Field(query, "hasArgs")!.Name, Is.EqualTo("hasArgs"));
        Assert.That(schema.Field(query, "hasArgs")!.Name, Is.EqualTo("hasArgs"));
        Assert.That(schema.Field(query, "nope"), Is.Null);
        Assert.That(schema.Field(null, "hasArgs"), Is.Null);

        var input = schema.Find("PetInput")!;
        Assert.That(schema.InputField(input, "name")!.Name, Is.EqualTo("name"));
        Assert.That(schema.InputField(input, "nope"), Is.Null);
        Assert.That(schema.InputField(null, "name"), Is.Null);

        var color = schema.Find("Color")!;
        Assert.That(schema.EnumValue(color, "RED")!.Name, Is.EqualTo("RED"));
        Assert.That(schema.EnumValue(color, "MAUVE"), Is.Null);
        Assert.That(schema.EnumValue(null, "RED"), Is.Null);

        Assert.That(schema.Directive("repeat")!.Name, Is.EqualTo("repeat"));
        Assert.That(schema.Directive("nope"), Is.Null);
    }

    /// <summary>A type has fields or input fields, never both. Asking for the other gives nothing.</summary>
    [Test]
    public void TheWrongKindOfMemberIsNotFound()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var schema = SchemaIndex.Parse(document.RootElement)!;

        var query = schema.Find("Query")!;

        Assert.That(schema.InputField(query, "hasArgs"), Is.Null);
        Assert.That(schema.EnumValue(query, "hasArgs"), Is.Null);
    }
}
