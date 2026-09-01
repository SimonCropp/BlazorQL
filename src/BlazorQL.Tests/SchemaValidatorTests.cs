/// <summary>
/// The validation rules, which BlazorQL owns outright now that the GraphQL.NET dependency is gone.
/// The fixture schema has a union, an interface, an enum, an input object, a required argument and a
/// deprecated field, which between them reach every rule below.
/// </summary>
[TestFixture]
public class SchemaValidatorTests
{
    static SchemaValidator Validator()
    {
        var json = File.ReadAllText(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "DocExplorerTests.schema.json"));
        using var document = JsonDocument.Parse(json);
        return new(SchemaIndex.Parse(document.RootElement)!);
    }

    static IReadOnlyList<OperationDiagnostic> Validate(string query) =>
        Validator().Validate(DocumentInfo.Parse(query));

    static IEnumerable<string> Errors(string query) =>
        Validate(query).Where(_ => _.IsError).Select(_ => _.Message);

    static IEnumerable<string> Warnings(string query) =>
        Validate(query).Where(_ => !_.IsError).Select(_ => _.Message);

    /// <summary>
    /// Nothing in the fixture schema is genuinely required — every non-null argument and input field
    /// there carries a default — so the required-ness rules need a schema of their own.
    /// </summary>
    const string requiredSchema =
        """
        {
          "__schema": {
            "queryType": {"name": "Query"},
            "types": [
              {"kind": "OBJECT", "name": "Query", "fields": [
                {"name": "need", "args": [
                  {"name": "arg", "type": {"kind": "NON_NULL", "ofType": {"kind": "SCALAR", "name": "String"}}, "defaultValue": null, "isDeprecated": false}
                ], "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false},
                {"name": "obj", "args": [
                  {"name": "in", "type": {"kind": "INPUT_OBJECT", "name": "In"}, "defaultValue": null, "isDeprecated": false}
                ], "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false}
              ]},
              {"kind": "SCALAR", "name": "String"},
              {"kind": "INPUT_OBJECT", "name": "In", "inputFields": [
                {"name": "req", "type": {"kind": "NON_NULL", "ofType": {"kind": "SCALAR", "name": "String"}}, "defaultValue": null, "isDeprecated": false}
              ]}
            ],
            "directives": []
          }
        }
        """;

    static IEnumerable<string> RequiredSchemaErrors(string query)
    {
        using var document = JsonDocument.Parse(requiredSchema);
        var validator = new SchemaValidator(SchemaIndex.Parse(document.RootElement)!);
        return validator.Validate(DocumentInfo.Parse(query))
            .Where(_ => _.IsError)
            .Select(_ => _.Message)
            .ToList();
    }

    [Test]
    public void AcceptsAValidOperation() =>
        Assert.That(Errors("{ person { name friends { name } } }"), Is.Empty);

    [Test]
    public void ReportsASyntaxError() =>
        Assert.That(Errors("{ person {"), Has.Some.Contains("Syntax Error"));

    // FieldsOnCorrectType

    [Test]
    public void FlagsAnUnknownField() =>
        Assert.That(Errors("{ nope }"), Has.Some.Contains("Cannot query field \"nope\" on type \"Query\"."));

    [Test]
    public void FlagsAnUnknownFieldOnANestedType() =>
        Assert.That(Errors("{ person { nope } }"), Has.Some.Contains("Cannot query field \"nope\" on type \"Person\"."));

    /// <summary>A union has no fields of its own, so the useful advice is to narrow first.</summary>
    [Test]
    public void SuggestsAnInlineFragmentOnAUnion() =>
        Assert.That(Errors("{ search(term: \"x\") { title } }"), Has.Some.Contains("inline fragment"));

    [Test]
    public void AllowsTypenameAnywhere() =>
        Assert.That(Errors("{ __typename person { __typename } }"), Is.Empty);

    [Test]
    public void AllowsSchemaIntrospectionOnTheRoot() =>
        Assert.That(Errors("{ __schema { queryType { name } } }"), Is.Empty);

    // ScalarLeafs

    [Test]
    public void FlagsASelectionOnAScalar() =>
        Assert.That(
            Errors("{ person { name { nope } } }"),
            Has.Some.Contains("must not have a selection since type \"String\" has no subfields"));

    [Test]
    public void FlagsAMissingSelectionOnAComposite() =>
        Assert.That(Errors("{ person }"), Has.Some.Contains("must have a selection of subfields"));

    // KnownArgumentNames and ProvidedRequiredArguments

    [Test]
    public void FlagsAnUnknownArgument() =>
        Assert.That(
            Errors("{ hasArgs(nope: 1) }"),
            Has.Some.Contains("Unknown argument \"nope\" on field \"Query.hasArgs\"."));

    /// <summary>
    /// The fixture's term argument is non-null but carries a default, which per spec makes it
    /// optional. Getting this backwards would put an error on most well-formed queries.
    /// </summary>
    [Test]
    public void AcceptsAnOmittedNonNullArgumentThatHasADefault() =>
        Assert.That(Errors("{ search { __typename } }"), Is.Empty);

    [Test]
    public void AcceptsAProvidedRequiredArgument() =>
        Assert.That(Errors("{ search(term: \"x\") { __typename } }"), Is.Empty);

    [Test]
    public void FlagsAMissingRequiredArgument() =>
        Assert.That(
            RequiredSchemaErrors("{ need }"),
            Has.Some.Contains("argument \"arg\" of type \"String!\" is required"));

    [Test]
    public void AcceptsARequiredArgumentWhenProvided() =>
        Assert.That(RequiredSchemaErrors("{ need(arg: \"x\") }"), Is.Empty);

    // ValuesOfCorrectType

    [Test]
    public void FlagsAStringWhereAnIntIsExpected() =>
        Assert.That(Errors("{ hasArgs(count: \"nope\") }"), Has.Some.Contains("Int cannot represent"));

    [Test]
    public void FlagsAnIntWhereAStringIsExpected() =>
        Assert.That(Errors("{ hasArgs(string: 1) }"), Has.Some.Contains("String cannot represent"));

    [Test]
    public void FlagsNullForANonNullArgument() =>
        Assert.That(Errors("{ search(term: null) { __typename } }"), Has.Some.Contains("found null"));

    [Test]
    public void FlagsAnUnknownInputObjectField() =>
        Assert.That(
            Errors("{ hasArgs(input: {name: \"a\", nope: 1}) }"),
            Has.Some.Contains("Field \"nope\" is not defined by type \"PetInput\"."));

    /// <summary>PetInput.name is non-null with a default, so omitting it is legal.</summary>
    [Test]
    public void AcceptsAnOmittedInputFieldThatHasADefault() =>
        Assert.That(Errors("{ hasArgs(input: {age: 1}) }"), Is.Empty);

    [Test]
    public void FlagsAMissingRequiredInputObjectField() =>
        Assert.That(
            RequiredSchemaErrors("{ obj(in: {}) }"),
            Has.Some.Contains("of required type \"String!\" was not provided"));

    [Test]
    public void AcceptsAWellFormedInputObject() =>
        Assert.That(Errors("{ hasArgs(input: {name: \"a\", age: 1}) }"), Is.Empty);

    // Variables

    [Test]
    public void AcceptsAVariableInAMatchingPosition() =>
        Assert.That(Errors("query Q($t: String!) { search(term: $t) { __typename } }"), Is.Empty);

    [Test]
    public void FlagsAnUndefinedVariable() =>
        Assert.That(
            Errors("{ search(term: $t) { __typename } }"),
            Has.Some.Contains("Variable \"$t\" is not defined."));

    [Test]
    public void FlagsAnUnusedVariable() =>
        Assert.That(
            Errors("query Q($t: String!) { person { name } }"),
            Has.Some.Contains("Variable \"$t\" is never used."));

    /// <summary>A nullable variable cannot fill a non-null position; the reverse is fine.</summary>
    [Test]
    public void FlagsAVariableOfTheWrongNullability() =>
        Assert.That(
            Errors("query Q($t: String) { search(term: $t) { __typename } }"),
            Has.Some.Contains("used in position expecting type \"String!\""));

    [Test]
    public void AcceptsANonNullVariableInANullablePosition() =>
        Assert.That(Errors("query Q($s: String!) { hasArgs(string: $s) }"), Is.Empty);

    [Test]
    public void FlagsAVariableOfTheWrongType() =>
        Assert.That(
            Errors("query Q($t: Int!) { search(term: $t) { __typename } }"),
            Has.Some.Contains("used in position expecting type \"String!\""));

    [Test]
    public void FlagsAVariableDeclaredAsANonInputType() =>
        Assert.That(
            Errors("query Q($p: Person) { person { name } }"),
            Has.Some.Contains("cannot be non-input type"));

    [Test]
    public void FlagsAVariableDeclaredAsAnUnknownType() =>
        Assert.That(Errors("query Q($p: Nope) { person { name } }"), Has.Some.Contains("Unknown type \"Nope\"."));

    // Fragments

    [Test]
    public void AcceptsASpreadOfADefinedFragment() =>
        Assert.That(Errors("{ person { ...F } } fragment F on Person { name }"), Is.Empty);

    [Test]
    public void FlagsAnUnknownFragment() =>
        Assert.That(Errors("{ person { ...F } }"), Has.Some.Contains("Unknown fragment \"F\"."));

    [Test]
    public void FlagsAnUnusedFragment() =>
        Assert.That(
            Errors("{ person { name } } fragment F on Person { name }"),
            Has.Some.Contains("Fragment \"F\" is never used."));

    [Test]
    public void FlagsAFragmentOnANonCompositeType() =>
        Assert.That(
            Errors("{ person { ...F } } fragment F on String { name }"),
            Has.Some.Contains("cannot condition on non composite type \"String\""));

    [Test]
    public void ValidatesInsideAFragment() =>
        Assert.That(
            Errors("{ person { ...F } } fragment F on Person { nope }"),
            Has.Some.Contains("Cannot query field \"nope\" on type \"Person\"."));

    [Test]
    public void ValidatesInsideAnInlineFragment() =>
        Assert.That(
            Errors("{ search(term: \"x\") { ... on Post { nope } } }"),
            Has.Some.Contains("Cannot query field \"nope\" on type \"Post\"."));

    [Test]
    public void AcceptsAnInlineFragmentNarrowingAUnion() =>
        Assert.That(Errors("{ search(term: \"x\") { ... on Post { title } } }"), Is.Empty);

    // Operations

    [Test]
    public void FlagsTwoOperationsWithTheSameName() =>
        Assert.That(
            Errors("query Q { person { name } } query Q { person { name } }"),
            Has.Some.Contains("There can be only one operation named \"Q\"."));

    [Test]
    public void FlagsAnAnonymousOperationBesideAnother() =>
        Assert.That(
            Errors("{ person { name } } query Q { person { name } }"),
            Has.Some.Contains("must be the only defined operation"));

    [Test]
    public void FlagsAnOperationTypeTheSchemaLacks() =>
        Assert.That(
            Errors("mutation { person { name } }"),
            Has.Some.Contains("Schema is not configured for mutations."));

    // Directives

    [Test]
    public void FlagsAnUnknownDirective() =>
        Assert.That(Errors("{ person @nope { name } }"), Has.Some.Contains("Unknown directive \"@nope\"."));

    // Deprecation warnings

    [Test]
    public void WarnsOnADeprecatedFieldWithoutErroring()
    {
        Assert.That(Warnings("{ oldField }"), Has.Some.Contains("deprecated"));
        Assert.That(Errors("{ oldField }"), Is.Empty);
    }

    [Test]
    public void WarnsOnADeprecatedArgument() =>
        Assert.That(Warnings("{ hasArgs(deprecatedArg: \"x\") }"), Has.Some.Contains("deprecated"));

    /// <summary>Diagnostics carry one-based line and column, which is what Monaco marks with.</summary>
    [Test]
    public void ReportsAOneBasedPosition()
    {
        var diagnostic = Validate("{\n  nope\n}").Single(_ => _.IsError);

        Assert.That(diagnostic.Line, Is.EqualTo(2));
        Assert.That(diagnostic.Column, Is.EqualTo(3));
    }
}
