/// <summary>
/// The validation rules, which BlazorQL owns outright now that the GraphQL.NET dependency is gone.
/// Most run against the shared doc-explorer fixture, which has a union, an interface, an input
/// object and a deprecated field. The rules it cannot reach — required-ness, and the value checks
/// over enums, lists, directive arguments and the remaining built-in scalars — carry their own
/// schemas below.
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
                {"name": "req", "type": {"kind": "NON_NULL", "ofType": {"kind": "SCALAR", "name": "String"}}, "defaultValue": null, "isDeprecated": false},
                {"name": "opt", "type": {"kind": "SCALAR", "name": "String"}, "defaultValue": null, "isDeprecated": false}
              ]}
            ],
            "directives": []
          }
        }
        """;

    /// <summary>
    /// The value rules need shapes the shared fixture does not carry: an enum-typed argument, a list
    /// argument, a directive that takes arguments, and the built-in scalars nothing else uses.
    /// Extending the fixture instead would rewrite the doc-explorer snapshots that share it.
    /// </summary>
    const string valuesSchema =
        """
        {
          "__schema": {
            "queryType": {"name": "Query"},
            "types": [
              {"kind": "OBJECT", "name": "Query", "fields": [
                {"name": "paint", "args": [
                  {"name": "color", "type": {"kind": "ENUM", "name": "Color"}, "defaultValue": null, "isDeprecated": false}
                ], "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false},
                {"name": "pick", "args": [
                  {"name": "ids", "type": {"kind": "LIST", "ofType": {"kind": "SCALAR", "name": "Int"}}, "defaultValue": null, "isDeprecated": false}
                ], "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false},
                {"name": "scalars", "args": [
                  {"name": "float", "type": {"kind": "SCALAR", "name": "Float"}, "defaultValue": null, "isDeprecated": false},
                  {"name": "flag", "type": {"kind": "SCALAR", "name": "Boolean"}, "defaultValue": null, "isDeprecated": false},
                  {"name": "id", "type": {"kind": "SCALAR", "name": "ID"}, "defaultValue": null, "isDeprecated": false},
                  {"name": "json", "type": {"kind": "SCALAR", "name": "JSON"}, "defaultValue": null, "isDeprecated": false}
                ], "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false}
              ]},
              {"kind": "SCALAR", "name": "String"},
              {"kind": "SCALAR", "name": "Int"},
              {"kind": "SCALAR", "name": "Float"},
              {"kind": "SCALAR", "name": "Boolean"},
              {"kind": "SCALAR", "name": "ID"},
              {"kind": "SCALAR", "name": "JSON"},
              {"kind": "ENUM", "name": "Color", "enumValues": [
                {"name": "RED", "isDeprecated": false},
                {"name": "GRAY", "isDeprecated": true, "deprecationReason": "Use RED."}
              ]}
            ],
            "directives": [
              {"name": "tag", "locations": ["FIELD"], "args": [
                {"name": "name", "type": {"kind": "SCALAR", "name": "String"}, "defaultValue": null, "isDeprecated": false}
              ]}
            ]
          }
        }
        """;

    static IReadOnlyList<OperationDiagnostic> Diagnostics(string schema, string query)
    {
        using var document = JsonDocument.Parse(schema);
        var validator = new SchemaValidator(SchemaIndex.Parse(document.RootElement)!);
        return validator.Validate(DocumentInfo.Parse(query));
    }

    static IEnumerable<string> RequiredSchemaErrors(string query) =>
        Diagnostics(requiredSchema, query)
            .Where(_ => _.IsError)
            .Select(_ => _.Message)
            .ToList();

    static IEnumerable<string> ValuesSchemaErrors(string query) =>
        Diagnostics(valuesSchema, query)
            .Where(_ => _.IsError)
            .Select(_ => _.Message)
            .ToList();

    static IEnumerable<string> ValuesSchemaWarnings(string query) =>
        Diagnostics(valuesSchema, query)
            .Where(_ => !_.IsError)
            .Select(_ => _.Message)
            .ToList();

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

    /// <summary>__typename is a String, so it cannot be selected into either.</summary>
    [Test]
    public void FlagsASelectionOnTypename() =>
        Assert.That(
            Errors("{ __typename { nope } }"),
            Has.Some.Contains("must not have a selection since type \"String\" has no subfields"));

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

    [Test]
    public void AcceptsNullForANullableArgument() =>
        Assert.That(Errors("{ hasArgs(string: null) }"), Is.Empty);

    [Test]
    public void FlagsANonObjectValueForAnInputObject() =>
        Assert.That(
            Errors("{ hasArgs(input: 1) }"),
            Has.Some.Contains("Expected value of type \"PetInput\", found a non-object value."));

    /// <summary>
    /// An empty object literal satisfies the required-field check vacuously, so the miss has to be
    /// provoked with a sibling field present.
    /// </summary>
    [Test]
    public void FlagsAMissingRequiredInputObjectFieldBesideAProvidedOne() =>
        Assert.That(
            RequiredSchemaErrors("{ obj(in: {opt: \"x\"}) }"),
            Has.Some.Contains("Field \"In.req\" of required type \"String!\" was not provided."));

    // Enums

    [Test]
    public void AcceptsAKnownEnumValue() =>
        Assert.That(ValuesSchemaErrors("{ paint(color: RED) }"), Is.Empty);

    [Test]
    public void FlagsANonEnumValueForAnEnum() =>
        Assert.That(
            ValuesSchemaErrors("{ paint(color: \"RED\") }"),
            Has.Some.Contains("Enum \"Color\" cannot represent non-enum value."));

    [Test]
    public void FlagsAnUnknownEnumValue() =>
        Assert.That(
            ValuesSchemaErrors("{ paint(color: BLUE) }"),
            Has.Some.Contains("Value \"BLUE\" does not exist in \"Color\" enum."));

    // Lists

    [Test]
    public void AcceptsAWellFormedListLiteral() =>
        Assert.That(ValuesSchemaErrors("{ pick(ids: [1, 2]) }"), Is.Empty);

    [Test]
    public void ChecksEveryElementOfAListLiteral() =>
        Assert.That(
            ValuesSchemaErrors("{ pick(ids: [1, \"nope\"]) }"),
            Has.Some.Contains("Int cannot represent"));

    /// <summary>A single value coerces to a one-element list, per spec.</summary>
    [Test]
    public void AcceptsASingleValueWhereAListIsExpected() =>
        Assert.That(ValuesSchemaErrors("{ pick(ids: 1) }"), Is.Empty);

    /// <summary>Coercing does not excuse it from the element check.</summary>
    [Test]
    public void ChecksASingleValueCoercedToAList() =>
        Assert.That(ValuesSchemaErrors("{ pick(ids: \"nope\") }"), Has.Some.Contains("Int cannot represent"));

    // Scalars

    [Test]
    public void FlagsAStringWhereAFloatIsExpected() =>
        Assert.That(ValuesSchemaErrors("{ scalars(float: \"nope\") }"), Has.Some.Contains("Float cannot represent"));

    /// <summary>An integer literal is a legal Float.</summary>
    [Test]
    public void AcceptsAnIntWhereAFloatIsExpected() =>
        Assert.That(ValuesSchemaErrors("{ scalars(float: 1) }"), Is.Empty);

    [Test]
    public void FlagsAnIntWhereABooleanIsExpected() =>
        Assert.That(ValuesSchemaErrors("{ scalars(flag: 1) }"), Has.Some.Contains("Boolean cannot represent"));

    [Test]
    public void AcceptsABoolean() =>
        Assert.That(ValuesSchemaErrors("{ scalars(flag: true) }"), Is.Empty);

    [Test]
    public void FlagsABooleanWhereAnIdIsExpected() =>
        Assert.That(ValuesSchemaErrors("{ scalars(id: true) }"), Has.Some.Contains("ID cannot represent"));

    /// <summary>An ID accepts either spelling.</summary>
    [Test]
    public void AcceptsAStringOrAnIntForAnId()
    {
        Assert.That(ValuesSchemaErrors("{ scalars(id: \"a\") }"), Is.Empty);
        Assert.That(ValuesSchemaErrors("{ scalars(id: 1) }"), Is.Empty);
    }

    /// <summary>
    /// A custom scalar's literal grammar belongs to the server, so anything that parses passes
    /// rather than producing a false error.
    /// </summary>
    [Test]
    public void AcceptsAnyLiteralForACustomScalar() =>
        Assert.That(ValuesSchemaErrors("{ scalars(json: true) }"), Is.Empty);

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

    [Test]
    public void AcceptsAListVariableInAListPosition() =>
        Assert.That(ValuesSchemaErrors("query Q($ids: [Int]) { pick(ids: $ids) }"), Is.Empty);

    [Test]
    public void FlagsANonListVariableInAListPosition() =>
        Assert.That(
            ValuesSchemaErrors("query Q($id: Int) { pick(ids: $id) }"),
            Has.Some.Contains("Variable \"$id\" of type \"Int\" used in position expecting type \"[Int]\"."));

    /// <summary>The declared type is rendered from the AST, so list nesting has to survive it.</summary>
    [Test]
    public void RendersAListTypeInAVariablePositionError() =>
        Assert.That(
            ValuesSchemaErrors("query Q($ids: [String]) { pick(ids: $ids) }"),
            Has.Some.Contains("Variable \"$ids\" of type \"[String]\" used in position expecting type \"[Int]\"."));

    [Test]
    public void FlagsAListVariableInAScalarPosition() =>
        Assert.That(
            ValuesSchemaErrors("query Q($ids: [Int]) { scalars(id: $ids) }"),
            Has.Some.Contains("Variable \"$ids\" of type \"[Int]\" used in position expecting type \"ID\"."));

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

    [Test]
    public void FlagsAFragmentOnAnUnknownType() =>
        Assert.That(
            Errors("{ person { ...F } } fragment F on Nope { name }"),
            Has.Some.Contains("Unknown type \"Nope\"."));

    /// <summary>An inline fragment without a type condition keeps the enclosing type.</summary>
    [Test]
    public void ValidatesAnInlineFragmentWithoutATypeCondition()
    {
        Assert.That(Errors("{ person { ... { name } } }"), Is.Empty);
        Assert.That(
            Errors("{ person { ... { nope } } }"),
            Has.Some.Contains("Cannot query field \"nope\" on type \"Person\"."));
    }

    [Test]
    public void FlagsAnInlineFragmentOnAnUnknownType() =>
        Assert.That(
            Errors("{ person { ... on Nope { name } } }"),
            Has.Some.Contains("Unknown type \"Nope\"."));

    /// <summary>The inline wording drops the fragment name that the named form carries.</summary>
    [Test]
    public void FlagsAnInlineFragmentOnANonCompositeType() =>
        Assert.That(
            Errors("{ person { ... on String { name } } }"),
            Has.Some.Contains("Fragment cannot condition on non composite type \"String\"."));

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

    [Test]
    public void FlagsASubscriptionTheSchemaLacks() =>
        Assert.That(
            Errors("subscription { person { name } }"),
            Has.Some.Contains("Schema is not configured for subscriptions."));

    // Directives

    [Test]
    public void FlagsAnUnknownDirective() =>
        Assert.That(Errors("{ person @nope { name } }"), Has.Some.Contains("Unknown directive \"@nope\"."));

    [Test]
    public void AcceptsAWellFormedDirectiveArgument() =>
        Assert.That(ValuesSchemaErrors("{ paint @tag(name: \"x\") }"), Is.Empty);

    [Test]
    public void FlagsAnUnknownArgumentOnADirective() =>
        Assert.That(
            ValuesSchemaErrors("{ paint @tag(nope: \"x\") }"),
            Has.Some.Contains("Unknown argument \"nope\" on directive \"@tag\"."));

    [Test]
    public void FlagsABadDirectiveArgumentValue() =>
        Assert.That(ValuesSchemaErrors("{ paint @tag(name: 1) }"), Has.Some.Contains("String cannot represent"));

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

    [Test]
    public void WarnsOnADeprecatedEnumValue() =>
        Assert.That(
            ValuesSchemaWarnings("{ paint(color: GRAY) }"),
            Has.Some.Contains("The enum value Color.GRAY is deprecated. Use RED."));

    /// <summary>Diagnostics carry one-based line and column, which is what Monaco marks with.</summary>
    [Test]
    public void ReportsAOneBasedPosition()
    {
        var diagnostic = Validate("{\n  nope\n}").Single(_ => _.IsError);

        Assert.That(diagnostic.Line, Is.EqualTo(2));
        Assert.That(diagnostic.Column, Is.EqualTo(3));
    }
}
