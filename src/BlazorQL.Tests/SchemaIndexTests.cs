
public class SchemaIndexTests
{
    static string SchemaJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ProjectFiles.DocExplorerTests_schema_json));

    [Test]
    public async Task ParsesAWrappedIntrospectionResult()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var schema = SchemaIndex.Parse(document.RootElement)!;

        await Assert.That(schema).IsNotNull();
        await Assert.That(schema.QueryTypeName).IsEqualTo("Query");
        await Assert.That(schema.MutationTypeName).IsNull();
        await Assert.That(schema.Description).Contains("hand-written");
        await Assert.That(schema.IsRootType("Query")).IsTrue();
        await Assert.That(schema.IsRootType("Person")).IsFalse();

        var person = schema.Find("Person")!;
        await Assert.That(person).IsNotNull();
        await Assert.That(person.Kind).IsEqualTo("OBJECT");
        await Assert.That(person.Interfaces!.Single().Name).IsEqualTo("Named");

        var query = schema.Find("Query")!;
        var hasArgs = query.Fields!.Single(_ => _.Name == "hasArgs");
        await Assert.That(hasArgs.Args.Single(_ => _.Name == "count").DefaultValue).IsEqualTo("0");
        var deprecatedArg = hasArgs.Args.Single(_ => _.Name == "deprecatedArg");
        await Assert.That(deprecatedArg.IsDeprecated).IsTrue();
        await Assert.That(deprecatedArg.DeprecationReason).Contains("instead");

        var friends = person.Fields!.Single(_ => _.Name == "friends");
        await Assert.That(friends.Type.Display()).IsEqualTo("[Person]");
        await Assert.That(friends.Type.Unwrap().Name).IsEqualTo("Person");

        var color = schema.Find("Color")!;
        await Assert.That(color.EnumValues!.Single(_ => _.IsDeprecated).Name).IsEqualTo("GRAY");

        var petInput = schema.Find("PetInput")!;
        await Assert.That(petInput.InputFields!.Single(_ => _.Name == "name").DefaultValue).IsEqualTo("\"Rex\"");

        await Assert.That(schema.Find("JSON")!.SpecifiedByURL).IsEqualTo("https://example.com/json-spec");

        var directive = schema.Directives.Single();
        await Assert.That(directive.Name).IsEqualTo("repeat");
        await Assert.That(directive.IsRepeatable).IsTrue();
        await Assert.That(directive.Locations.Single()).IsEqualTo("FIELD");
    }

    [Test]
    public async Task ParsesABareIntrospectionResult()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var bare = document.RootElement.GetProperty("data");
        var schema = SchemaIndex.Parse(bare)!;

        await Assert.That(schema).IsNotNull();
        await Assert.That(schema.QueryTypeName).IsEqualTo("Query");
    }

    [Test]
    public async Task ReturnsNullWhenTheShapeIsNotIntrospection()
    {
        using var document = JsonDocument.Parse("""{"data": {"something": 1}}""");
        await Assert.That(SchemaIndex.Parse(document.RootElement)).IsNull();
    }

    /// <summary>
    /// The member lookups the language layer resolves names through. Built lazily per type, so the
    /// first ask and every one after it have to agree.
    /// </summary>
    [Test]
    public async Task MemberLookupsFindWhatAScanWouldHave()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var schema = SchemaIndex.Parse(document.RootElement)!;

        var query = schema.Find("Query")!;
        await Assert.That(schema.Field(query, "hasArgs")!.Name).IsEqualTo("hasArgs");
        await Assert.That(schema.Field(query, "hasArgs")!.Name).IsEqualTo("hasArgs");
        await Assert.That(schema.Field(query, "nope")).IsNull();
        await Assert.That(schema.Field(null, "hasArgs")).IsNull();

        var input = schema.Find("PetInput")!;
        await Assert.That(schema.InputField(input, "name")!.Name).IsEqualTo("name");
        await Assert.That(schema.InputField(input, "nope")).IsNull();
        await Assert.That(schema.InputField(null, "name")).IsNull();

        var color = schema.Find("Color")!;
        await Assert.That(schema.EnumValue(color, "RED")!.Name).IsEqualTo("RED");
        await Assert.That(schema.EnumValue(color, "MAUVE")).IsNull();
        await Assert.That(schema.EnumValue(null, "RED")).IsNull();

        await Assert.That(schema.Directive("repeat")!.Name).IsEqualTo("repeat");
        await Assert.That(schema.Directive("nope")).IsNull();
    }

    /// <summary>A type has fields or input fields, never both. Asking for the other gives nothing.</summary>
    [Test]
    public async Task TheWrongKindOfMemberIsNotFound()
    {
        using var document = JsonDocument.Parse(SchemaJson());
        var schema = SchemaIndex.Parse(document.RootElement)!;

        var query = schema.Find("Query")!;

        await Assert.That(schema.InputField(query, "hasArgs")).IsNull();
        await Assert.That(schema.EnumValue(query, "hasArgs")).IsNull();
    }
}