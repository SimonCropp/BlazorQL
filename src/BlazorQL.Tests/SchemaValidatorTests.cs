/// <summary>
/// The validation rules, which BlazorQL owns outright now that the GraphQL.NET dependency is gone.
/// Most run against the shared doc-explorer fixture, which has a union, an interface, an input
/// object and a deprecated field. The rules it cannot reach — required-ness, and the value checks
/// over enums, lists, directive arguments and the remaining built-in scalars — carry their own
/// schemas below.
/// </summary>
public class SchemaValidatorTests
{
    static SchemaValidator Validator()
    {
        var json = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, ProjectFiles.DocExplorerTests_schema_json));
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
    public async Task AcceptsAValidOperation() =>
        await Assert.That(Errors("{ person { name friends { name } } }")).IsEmpty();

    [Test]
    public async Task ReportsASyntaxError() =>
        await Assert.That(Errors("{ person {")).Contains(_ => _.Contains("Syntax Error"));

    // FieldsOnCorrectType

    [Test]
    public async Task FlagsAnUnknownField() =>
        await Assert.That(Errors("{ nope }")).Contains(_ => _.Contains("Cannot query field \"nope\" on type \"Query\"."));

    [Test]
    public async Task FlagsAnUnknownFieldOnANestedType() =>
        await Assert.That(Errors("{ person { nope } }")).Contains(_ => _.Contains("Cannot query field \"nope\" on type \"Person\"."));

    /// <summary>A union has no fields of its own, so the useful advice is to narrow first.</summary>
    [Test]
    public async Task SuggestsAnInlineFragmentOnAUnion() =>
        await Assert.That(Errors("{ search(term: \"x\") { title } }")).Contains(_ => _.Contains("inline fragment"));

    [Test]
    public async Task AllowsTypenameAnywhere() =>
        await Assert.That(Errors("{ __typename person { __typename } }")).IsEmpty();

    [Test]
    public async Task AllowsSchemaIntrospectionOnTheRoot() =>
        await Assert.That(Errors("{ __schema { queryType { name } } }")).IsEmpty();

    // ScalarLeafs

    [Test]
    public async Task FlagsASelectionOnAScalar() =>
        await Assert.That(Errors("{ person { name { nope } } }"))
            .Contains(_ => _.Contains("must not have a selection since type \"String\" has no subfields"));

    [Test]
    public async Task FlagsAMissingSelectionOnAComposite() =>
        await Assert.That(Errors("{ person }")).Contains(_ => _.Contains("must have a selection of subfields"));

    /// <summary>__typename is a String, so it cannot be selected into either.</summary>
    [Test]
    public async Task FlagsASelectionOnTypename() =>
        await Assert.That(Errors("{ __typename { nope } }"))
            .Contains(_ => _.Contains("must not have a selection since type \"String\" has no subfields"));

    // KnownArgumentNames and ProvidedRequiredArguments

    [Test]
    public async Task FlagsAnUnknownArgument() =>
        await Assert.That(Errors("{ hasArgs(nope: 1) }"))
            .Contains(_ => _.Contains("Unknown argument \"nope\" on field \"Query.hasArgs\"."));

    /// <summary>
    /// The fixture's term argument is non-null but carries a default, which per spec makes it
    /// optional. Getting this backwards would put an error on most well-formed queries.
    /// </summary>
    [Test]
    public async Task AcceptsAnOmittedNonNullArgumentThatHasADefault() =>
        await Assert.That(Errors("{ search { __typename } }")).IsEmpty();

    [Test]
    public async Task AcceptsAProvidedRequiredArgument() =>
        await Assert.That(Errors("{ search(term: \"x\") { __typename } }")).IsEmpty();

    [Test]
    public async Task FlagsAMissingRequiredArgument() =>
        await Assert.That(RequiredSchemaErrors("{ need }"))
            .Contains(_ => _.Contains("argument \"arg\" of type \"String!\" is required"));

    [Test]
    public async Task AcceptsARequiredArgumentWhenProvided() =>
        await Assert.That(RequiredSchemaErrors("{ need(arg: \"x\") }")).IsEmpty();

    // ValuesOfCorrectType

    [Test]
    public async Task FlagsAStringWhereAnIntIsExpected() =>
        await Assert.That(Errors("{ hasArgs(count: \"nope\") }")).Contains(_ => _.Contains("Int cannot represent"));

    [Test]
    public async Task FlagsAnIntWhereAStringIsExpected() =>
        await Assert.That(Errors("{ hasArgs(string: 1) }")).Contains(_ => _.Contains("String cannot represent"));

    [Test]
    public async Task FlagsNullForANonNullArgument() =>
        await Assert.That(Errors("{ search(term: null) { __typename } }")).Contains(_ => _.Contains("found null"));

    [Test]
    public async Task FlagsAnUnknownInputObjectField() =>
        await Assert.That(Errors("{ hasArgs(input: {name: \"a\", nope: 1}) }"))
            .Contains(_ => _.Contains("Field \"nope\" is not defined by type \"PetInput\"."));

    /// <summary>PetInput.name is non-null with a default, so omitting it is legal.</summary>
    [Test]
    public async Task AcceptsAnOmittedInputFieldThatHasADefault() =>
        await Assert.That(Errors("{ hasArgs(input: {age: 1}) }")).IsEmpty();

    [Test]
    public async Task FlagsAMissingRequiredInputObjectField() =>
        await Assert.That(RequiredSchemaErrors("{ obj(in: {}) }"))
            .Contains(_ => _.Contains("of required type \"String!\" was not provided"));

    [Test]
    public async Task AcceptsAWellFormedInputObject() =>
        await Assert.That(Errors("{ hasArgs(input: {name: \"a\", age: 1}) }")).IsEmpty();

    [Test]
    public async Task AcceptsNullForANullableArgument() =>
        await Assert.That(Errors("{ hasArgs(string: null) }")).IsEmpty();

    [Test]
    public async Task FlagsANonObjectValueForAnInputObject() =>
        await Assert.That(Errors("{ hasArgs(input: 1) }"))
            .Contains(_ => _.Contains("Expected value of type \"PetInput\", found a non-object value."));

    /// <summary>
    /// An empty object literal satisfies the required-field check vacuously, so the miss has to be
    /// provoked with a sibling field present.
    /// </summary>
    [Test]
    public async Task FlagsAMissingRequiredInputObjectFieldBesideAProvidedOne() =>
        await Assert.That(RequiredSchemaErrors("{ obj(in: {opt: \"x\"}) }"))
            .Contains(_ => _.Contains("Field \"In.req\" of required type \"String!\" was not provided."));

    // Enums

    [Test]
    public async Task AcceptsAKnownEnumValue() =>
        await Assert.That(ValuesSchemaErrors("{ paint(color: RED) }")).IsEmpty();

    [Test]
    public async Task FlagsANonEnumValueForAnEnum() =>
        await Assert.That(ValuesSchemaErrors("{ paint(color: \"RED\") }"))
            .Contains(_ => _.Contains("Enum \"Color\" cannot represent non-enum value."));

    [Test]
    public async Task FlagsAnUnknownEnumValue() =>
        await Assert.That(ValuesSchemaErrors("{ paint(color: BLUE) }"))
            .Contains(_ => _.Contains("Value \"BLUE\" does not exist in \"Color\" enum."));

    // Lists

    [Test]
    public async Task AcceptsAWellFormedListLiteral() =>
        await Assert.That(ValuesSchemaErrors("{ pick(ids: [1, 2]) }")).IsEmpty();

    [Test]
    public async Task ChecksEveryElementOfAListLiteral() =>
        await Assert.That(ValuesSchemaErrors("{ pick(ids: [1, \"nope\"]) }"))
            .Contains(_ => _.Contains("Int cannot represent"));

    /// <summary>A single value coerces to a one-element list, per spec.</summary>
    [Test]
    public async Task AcceptsASingleValueWhereAListIsExpected() =>
        await Assert.That(ValuesSchemaErrors("{ pick(ids: 1) }")).IsEmpty();

    /// <summary>Coercing does not excuse it from the element check.</summary>
    [Test]
    public async Task ChecksASingleValueCoercedToAList() =>
        await Assert.That(ValuesSchemaErrors("{ pick(ids: \"nope\") }")).Contains(_ => _.Contains("Int cannot represent"));

    // Scalars

    [Test]
    public async Task FlagsAStringWhereAFloatIsExpected() =>
        await Assert.That(ValuesSchemaErrors("{ scalars(float: \"nope\") }")).Contains(_ => _.Contains("Float cannot represent"));

    /// <summary>An integer literal is a legal Float.</summary>
    [Test]
    public async Task AcceptsAnIntWhereAFloatIsExpected() =>
        await Assert.That(ValuesSchemaErrors("{ scalars(float: 1) }")).IsEmpty();

    [Test]
    public async Task FlagsAnIntWhereABooleanIsExpected() =>
        await Assert.That(ValuesSchemaErrors("{ scalars(flag: 1) }")).Contains(_ => _.Contains("Boolean cannot represent"));

    [Test]
    public async Task AcceptsABoolean() =>
        await Assert.That(ValuesSchemaErrors("{ scalars(flag: true) }")).IsEmpty();

    [Test]
    public async Task FlagsABooleanWhereAnIdIsExpected() =>
        await Assert.That(ValuesSchemaErrors("{ scalars(id: true) }")).Contains(_ => _.Contains("ID cannot represent"));

    /// <summary>An ID accepts either spelling.</summary>
    [Test]
    public async Task AcceptsAStringOrAnIntForAnId()
    {
        await Assert.That(ValuesSchemaErrors("{ scalars(id: \"a\") }")).IsEmpty();
        await Assert.That(ValuesSchemaErrors("{ scalars(id: 1) }")).IsEmpty();
    }

    /// <summary>
    /// A custom scalar's literal grammar belongs to the server, so anything that parses passes
    /// rather than producing a false error.
    /// </summary>
    [Test]
    public async Task AcceptsAnyLiteralForACustomScalar() =>
        await Assert.That(ValuesSchemaErrors("{ scalars(json: true) }")).IsEmpty();

    // Variables

    [Test]
    public async Task AcceptsAVariableInAMatchingPosition() =>
        await Assert.That(Errors("query Q($t: String!) { search(term: $t) { __typename } }")).IsEmpty();

    [Test]
    public async Task FlagsAnUndefinedVariable() =>
        await Assert.That(Errors("{ search(term: $t) { __typename } }"))
            .Contains(_ => _.Contains("Variable \"$t\" is not defined."));

    [Test]
    public async Task FlagsAnUnusedVariable() =>
        await Assert.That(Errors("query Q($t: String!) { person { name } }"))
            .Contains(_ => _.Contains("Variable \"$t\" is never used."));

    /// <summary>
    /// A nullable variable cannot fill a non-null position; the reverse is fine. Spec 5.8.5 makes
    /// this the strict case: no default on the variable, and none on the argument either, so
    /// nothing guarantees a value. The shared fixture cannot express it — every non-null argument
    /// there carries a default, which is itself an escape from the rule.
    /// </summary>
    [Test]
    public async Task FlagsAVariableOfTheWrongNullability() =>
        await Assert.That(RequiredSchemaErrors("query Q($t: String) { need(arg: $t) }"))
            .Contains(_ => _.Contains("used in position expecting type \"String!\""));

    /// <summary>
    /// Spec 5.8.5: a nullable variable does fill a non-null position when the variable declares a
    /// default that is not null. <c>@skip(if: $flag)</c> with <c>$flag: Boolean = false</c> is the
    /// everyday shape of this, and rejecting it marked correct documents.
    /// </summary>
    [Test]
    public async Task AcceptsANullableVariableWithADefaultInANonNullPosition() =>
        await Assert.That(RequiredSchemaErrors("""query Q($t: String = "x") { need(arg: $t) }""")).IsEmpty();

    /// <summary>A default of the null literal guarantees nothing, so it is no escape.</summary>
    [Test]
    public async Task FlagsANullableVariableDefaultingToNullInANonNullPosition() =>
        await Assert.That(RequiredSchemaErrors("query Q($t: String = null) { need(arg: $t) }"))
            .Contains(_ => _.Contains("used in position expecting type \"String!\""));

    /// <summary>
    /// Spec 5.8.5's other escape: the argument itself declares a default, so omitting the variable
    /// falls back to it. <c>search(term:)</c> defaults to "all".
    /// </summary>
    [Test]
    public async Task AcceptsANullableVariableWhereTheArgumentHasADefault() =>
        await Assert.That(Errors("query Q($t: String) { search(term: $t) { __typename } }")).IsEmpty();

    [Test]
    public async Task AcceptsANonNullVariableInANullablePosition() =>
        await Assert.That(Errors("query Q($s: String!) { hasArgs(string: $s) }")).IsEmpty();

    [Test]
    public async Task FlagsAVariableOfTheWrongType() =>
        await Assert.That(Errors("query Q($t: Int!) { search(term: $t) { __typename } }"))
            .Contains(_ => _.Contains("used in position expecting type \"String!\""));

    [Test]
    public async Task FlagsAVariableDeclaredAsANonInputType() =>
        await Assert.That(Errors("query Q($p: Person) { person { name } }"))
            .Contains(_ => _.Contains("cannot be non-input type"));

    [Test]
    public async Task FlagsAVariableDeclaredAsAnUnknownType() =>
        await Assert.That(Errors("query Q($p: Nope) { person { name } }")).Contains(_ => _.Contains("Unknown type \"Nope\"."));

    [Test]
    public async Task AcceptsAListVariableInAListPosition() =>
        await Assert.That(ValuesSchemaErrors("query Q($ids: [Int]) { pick(ids: $ids) }")).IsEmpty();

    [Test]
    public async Task FlagsANonListVariableInAListPosition() =>
        await Assert.That(ValuesSchemaErrors("query Q($id: Int) { pick(ids: $id) }"))
            .Contains(_ => _.Contains("Variable \"$id\" of type \"Int\" used in position expecting type \"[Int]\"."));

    /// <summary>The declared type is rendered from the AST, so list nesting has to survive it.</summary>
    [Test]
    public async Task RendersAListTypeInAVariablePositionError() =>
        await Assert.That(ValuesSchemaErrors("query Q($ids: [String]) { pick(ids: $ids) }"))
            .Contains(_ => _.Contains("Variable \"$ids\" of type \"[String]\" used in position expecting type \"[Int]\"."));

    [Test]
    public async Task FlagsAListVariableInAScalarPosition() =>
        await Assert.That(ValuesSchemaErrors("query Q($ids: [Int]) { scalars(id: $ids) }"))
            .Contains(_ => _.Contains("Variable \"$ids\" of type \"[Int]\" used in position expecting type \"ID\"."));

    // Fragments

    [Test]
    public async Task AcceptsASpreadOfADefinedFragment() =>
        await Assert.That(Errors("{ person { ...F } } fragment F on Person { name }")).IsEmpty();

    [Test]
    public async Task FlagsAnUnknownFragment() =>
        await Assert.That(Errors("{ person { ...F } }")).Contains(_ => _.Contains("Unknown fragment \"F\"."));

    [Test]
    public async Task FlagsAnUnusedFragment() =>
        await Assert.That(Errors("{ person { name } } fragment F on Person { name }"))
            .Contains(_ => _.Contains("Fragment \"F\" is never used."));

    [Test]
    public async Task FlagsAFragmentOnANonCompositeType() =>
        await Assert.That(Errors("{ person { ...F } } fragment F on String { name }"))
            .Contains(_ => _.Contains("cannot condition on non composite type \"String\""));

    [Test]
    public async Task ValidatesInsideAFragment() =>
        await Assert.That(Errors("{ person { ...F } } fragment F on Person { nope }"))
            .Contains(_ => _.Contains("Cannot query field \"nope\" on type \"Person\"."));

    [Test]
    public async Task ValidatesInsideAnInlineFragment() =>
        await Assert.That(Errors("{ search(term: \"x\") { ... on Post { nope } } }"))
            .Contains(_ => _.Contains("Cannot query field \"nope\" on type \"Post\"."));

    [Test]
    public async Task AcceptsAnInlineFragmentNarrowingAUnion() =>
        await Assert.That(Errors("{ search(term: \"x\") { ... on Post { title } } }")).IsEmpty();

    [Test]
    public async Task FlagsAFragmentOnAnUnknownType() =>
        await Assert.That(Errors("{ person { ...F } } fragment F on Nope { name }"))
            .Contains(_ => _.Contains("Unknown type \"Nope\"."));

    /// <summary>An inline fragment without a type condition keeps the enclosing type.</summary>
    [Test]
    public async Task ValidatesAnInlineFragmentWithoutATypeCondition()
    {
        await Assert.That(Errors("{ person { ... { name } } }")).IsEmpty();
        await Assert.That(Errors("{ person { ... { nope } } }"))
            .Contains(_ => _.Contains("Cannot query field \"nope\" on type \"Person\"."));
    }

    [Test]
    public async Task FlagsAnInlineFragmentOnAnUnknownType() =>
        await Assert.That(Errors("{ person { ... on Nope { name } } }"))
            .Contains(_ => _.Contains("Unknown type \"Nope\"."));

    /// <summary>The inline wording drops the fragment name that the named form carries.</summary>
    [Test]
    public async Task FlagsAnInlineFragmentOnANonCompositeType() =>
        await Assert.That(Errors("{ person { ... on String { name } } }"))
            .Contains(_ => _.Contains("Fragment cannot condition on non composite type \"String\"."));

    // Operations

    [Test]
    public async Task FlagsTwoOperationsWithTheSameName() =>
        await Assert.That(Errors("query Q { person { name } } query Q { person { name } }"))
            .Contains(_ => _.Contains("There can be only one operation named \"Q\"."));

    [Test]
    public async Task FlagsAnAnonymousOperationBesideAnother() =>
        await Assert.That(Errors("{ person { name } } query Q { person { name } }"))
            .Contains(_ => _.Contains("must be the only defined operation"));

    [Test]
    public async Task FlagsAnOperationTypeTheSchemaLacks() =>
        await Assert.That(Errors("mutation { person { name } }"))
            .Contains(_ => _.Contains("Schema is not configured for mutations."));

    [Test]
    public async Task FlagsASubscriptionTheSchemaLacks() =>
        await Assert.That(Errors("subscription { person { name } }"))
            .Contains(_ => _.Contains("Schema is not configured for subscriptions."));

    // Directives

    [Test]
    public async Task FlagsAnUnknownDirective() =>
        await Assert.That(Errors("{ person @nope { name } }")).Contains(_ => _.Contains("Unknown directive \"@nope\"."));

    [Test]
    public async Task AcceptsAWellFormedDirectiveArgument() =>
        await Assert.That(ValuesSchemaErrors("{ paint @tag(name: \"x\") }")).IsEmpty();

    [Test]
    public async Task FlagsAnUnknownArgumentOnADirective() =>
        await Assert.That(ValuesSchemaErrors("{ paint @tag(nope: \"x\") }"))
            .Contains(_ => _.Contains("Unknown argument \"nope\" on directive \"@tag\"."));

    [Test]
    public async Task FlagsABadDirectiveArgumentValue() =>
        await Assert.That(ValuesSchemaErrors("{ paint @tag(name: 1) }")).Contains(_ => _.Contains("String cannot represent"));

    // Deprecation warnings

    [Test]
    public async Task WarnsOnADeprecatedFieldWithoutErroring()
    {
        await Assert.That(Warnings("{ oldField }")).Contains(_ => _.Contains("deprecated"));
        await Assert.That(Errors("{ oldField }")).IsEmpty();
    }

    [Test]
    public async Task WarnsOnADeprecatedArgument() =>
        await Assert.That(Warnings("{ hasArgs(deprecatedArg: \"x\") }")).Contains(_ => _.Contains("deprecated"));

    [Test]
    public async Task WarnsOnADeprecatedEnumValue() =>
        await Assert.That(ValuesSchemaWarnings("{ paint(color: GRAY) }"))
            .Contains(_ => _.Contains("The enum value Color.GRAY is deprecated. Use RED."));

    /// <summary>Diagnostics carry one-based line and column, which is what Monaco marks with.</summary>
    [Test]
    public async Task ReportsAOneBasedPosition()
    {
        var diagnostic = Validate("{\n  nope\n}").Single(_ => _.IsError);

        await Assert.That(diagnostic.Line).IsEqualTo(2);
        await Assert.That(diagnostic.Column).IsEqualTo(3);
    }
}