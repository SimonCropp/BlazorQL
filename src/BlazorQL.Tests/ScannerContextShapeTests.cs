/// <summary>
/// Document shapes the scanner resolved the wrong context for: a list inside an input object, an
/// inline fragment with no type condition, an object default on a variable, and an argument name
/// under ctrl/cmd-click. Each is asserted where the user meets it (completion, hover, jump to doc)
/// as well as on the scan.
/// </summary>
public class ScannerContextShapeTests
{
    /// <summary>
    /// Input objects holding lists — of enum values and of further input objects — which the shared
    /// schemas do not have. A mutation takes one, as the common create-input shape does.
    /// </summary>
    const string shapesSchema =
        """
        {
          "__schema": {
            "queryType": {"name": "Query"},
            "mutationType": {"name": "Mutation"},
            "types": [
              {"kind": "OBJECT", "name": "Query", "fields": [
                {"name": "issues", "type": {"kind": "OBJECT", "name": "Issue"}, "isDeprecated": false, "args": [
                  {"name": "states", "type": {"kind": "LIST", "ofType": {"kind": "ENUM", "name": "State"}}, "isDeprecated": false},
                  {"name": "filterBy", "type": {"kind": "INPUT_OBJECT", "name": "IssueFilters"}, "isDeprecated": false}
                ]},
                {"name": "viewer", "type": {"kind": "OBJECT", "name": "User"}, "isDeprecated": false, "args": []}
              ]},
              {"kind": "OBJECT", "name": "Mutation", "fields": [
                {"name": "createThing", "type": {"kind": "OBJECT", "name": "Issue"}, "isDeprecated": false, "args": [
                  {"name": "input", "type": {"kind": "INPUT_OBJECT", "name": "ThingInput"}, "isDeprecated": false}
                ]}
              ]},
              {"kind": "OBJECT", "name": "Issue", "fields": [
                {"name": "title", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false, "args": []}
              ]},
              {"kind": "OBJECT", "name": "User", "fields": [
                {"name": "id", "type": {"kind": "SCALAR", "name": "ID"}, "isDeprecated": false, "args": []},
                {"name": "login", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false, "args": []},
                {"name": "status", "type": {"kind": "OBJECT", "name": "Status"}, "isDeprecated": false, "args": []}
              ]},
              {"kind": "OBJECT", "name": "Status", "fields": [
                {"name": "emoji", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false, "args": []}
              ]},
              {"kind": "INPUT_OBJECT", "name": "IssueFilters", "inputFields": [
                {"name": "states", "type": {"kind": "LIST", "ofType": {"kind": "ENUM", "name": "State"}}, "isDeprecated": false},
                {"name": "assignee", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false},
                {"name": "labels", "type": {"kind": "LIST", "ofType": {"kind": "INPUT_OBJECT", "name": "LabelInput"}}, "isDeprecated": false}
              ]},
              {"kind": "INPUT_OBJECT", "name": "LabelInput", "inputFields": [
                {"name": "label", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false},
                {"name": "color", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false}
              ]},
              {"kind": "INPUT_OBJECT", "name": "ThingInput", "inputFields": [
                {"name": "name", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false},
                {"name": "items", "type": {"kind": "LIST", "ofType": {"kind": "NON_NULL", "ofType": {"kind": "INPUT_OBJECT", "name": "LabelInput"}}}, "isDeprecated": false}
              ]},
              {"kind": "ENUM", "name": "State", "enumValues": [
                {"name": "OPEN", "isDeprecated": false},
                {"name": "CLOSED", "isDeprecated": false}
              ]},
              {"kind": "SCALAR", "name": "String"},
              {"kind": "SCALAR", "name": "ID"},
              {"kind": "SCALAR", "name": "Boolean"}
            ],
            "directives": [
              {"name": "include", "locations": ["FIELD", "INLINE_FRAGMENT"], "args": [
                {"name": "if", "type": {"kind": "NON_NULL", "ofType": {"kind": "SCALAR", "name": "Boolean"}}, "isDeprecated": false}
              ]}
            ]
          }
        }
        """;

    readonly SchemaIndex schema = ContextScannerTests.Parse(shapesSchema);

    static (string Text, int Caret) Unmark(string marked)
    {
        var caret = marked.IndexOf('|');
        if (caret < 0)
        {
            throw new ArgumentException("the document needs a | caret marker", nameof(marked));
        }

        return (marked.Remove(caret, 1), caret);
    }

    ScanResult Scan(string marked)
    {
        var (text, caret) = Unmark(marked);
        return ContextScanner.Scan(schema, text, caret);
    }

    string[] Labels(string marked)
    {
        var (text, caret) = Unmark(marked);
        return [.. CompletionEngine.Complete(schema, text, caret).Select(_ => _.Label)];
    }

    // ---- A list inside an input object ----

    /// <summary>
    /// The bracket's values are the input field's, not the argument's: a list of an input field
    /// used to take the type of the argument the whole object is the value of.
    /// </summary>
    [Test]
    public async Task AListInAnInputObjectOffersTheInputFieldsValues()
    {
        string[] expected = ["OPEN", "CLOSED"];

        await Assert.That(Labels("{ issues(filterBy: {states: [|]}) { title } }"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>The top-level form already worked, and still does.</summary>
    [Test]
    public async Task AListArgumentStillOffersItsValues()
    {
        string[] expected = ["OPEN", "CLOSED"];

        await Assert.That(Labels("{ issues(states: [|]) { title } }"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnInputObjectInAListOfAnInputFieldIsThatFieldsType()
    {
        var scan = Scan("{ issues(filterBy: {labels: [{|}]}) { title } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("LabelInput");
    }

    /// <summary>The common create-input shape: a list of input objects one field into the input.</summary>
    [Test]
    public async Task AMutationInputsListOfInputObjectsOffersTheirFields()
    {
        string[] expected = ["label", "color"];

        await Assert.That(Labels("""mutation { createThing(input: {name: "x", items: [{|}]}) { title } }"""))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>Closing the inner list is a finished value, back at the input object's field names.</summary>
    [Test]
    public async Task AfterTheListTheInputObjectIsBackAtFieldNames()
    {
        var scan = Scan("{ issues(filterBy: {states: [OPEN], |}) { title } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("IssueFilters");
    }

    // ---- An inline fragment with no type condition ----

    /// <summary>
    /// With no type condition the fragment selects from the type it sits in. Its directive used to
    /// leave the ellipsis pending, so the brace took the previous sibling's type.
    /// </summary>
    [Test]
    public async Task ADirectedInlineFragmentSelectsFromTheEnclosingType()
    {
        var scan = Scan("query($x: Boolean!) { viewer { login ... @include(if: $x) { | } } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("User");
    }

    /// <summary>With no sibling before it there was no type at all.</summary>
    [Test]
    public async Task ADirectedInlineFragmentFirstInASelectionStillResolves()
    {
        var scan = Scan("query($x: Boolean!) { viewer { ... @include(if: $x) { | } } }");

        await Assert.That(scan.CurrentType!.Name).IsEqualTo("User");
    }

    [Test]
    public async Task ADirectedInlineFragmentAfterANestedSiblingIsNotThatSiblingsType()
    {
        var scan = Scan("query($x: Boolean!) { viewer { status { emoji } ... @include(if: $x) { | } } }");

        await Assert.That(scan.CurrentType!.Name).IsEqualTo("User");
    }

    /// <summary>The directive's argument list, not a fragment spread.</summary>
    [Test]
    public async Task ADirectiveOnAnInlineFragmentOffersItsArguments()
    {
        string[] expected = ["if"];

        await Assert.That(Labels("{ viewer { ... @include(|) { id } } }"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>The bare form, no directive and no type condition, is the same fragment.</summary>
    [Test]
    public async Task ABareInlineFragmentSelectsFromTheEnclosingType()
    {
        var scan = Scan("{ viewer { status { emoji } ... { | } } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("User");
    }

    /// <summary>The first field inside the bare form was read as a fragment's name.</summary>
    [Test]
    public async Task TheFirstFieldInABareInlineFragmentIsAField()
    {
        var scan = Scan("{ viewer { ... { status { | } } } }");

        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Status");
    }

    [Test]
    public async Task HoverInsideADirectedInlineFragmentShowsTheField()
    {
        var (text, caret) = Unmark("query($x: Boolean!) { viewer { login ... @include(if: $x) { i|d } } }");

        await Assert.That(HoverEngine.Hover(schema, text, caret)?.Markdown).Contains("User.id");
    }

    /// <summary>A type condition with a directive after it keeps working as before.</summary>
    [Test]
    public async Task ATypeConditionWithADirectiveStillSelectsItsType()
    {
        var scan = Scan("query($x: Boolean!) { viewer { ... on Status @include(if: $x) { | } } }");

        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Status");
    }

    /// <summary>And a named spread with a directive is still a spread, closed by its name.</summary>
    [Test]
    public async Task ANamedSpreadWithADirectiveLeavesTheSelectionAlone()
    {
        var scan = Scan("query($x: Boolean!) { viewer { ...Fields @include(if: $x) | } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("User");
    }

    // ---- An object default on a variable ----

    /// <summary>
    /// A variable's object default opened a selection at the document level, which used up the
    /// operation's root, and the operation's own selection then fell back to the query root.
    /// </summary>
    [Test]
    public async Task AnObjectDefaultLeavesTheOperationsRootAlone()
    {
        var scan = Scan("""mutation M($in: ThingInput = {name: "x"}) { | }""");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Mutation");
    }

    /// <summary>Inside the default, the fields on offer are the variable's type's.</summary>
    [Test]
    public async Task InsideAnObjectDefaultAreTheVariablesInputFields()
    {
        string[] expected = ["name", "items"];

        await Assert.That(Labels("mutation M($in: ThingInput! = {|}) { createThing(input: $in) { title } }"))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    /// <summary>A colon inside the default is an input field's, not a second variable's type.</summary>
    [Test]
    public async Task AColonInsideAnObjectDefaultIsNotAVariableType()
    {
        var scan = Scan("""mutation M($in: ThingInput = {name: "x", |}) { title }""");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("ThingInput");
    }

    /// <summary>The variables after an object default are still collected, and still typed.</summary>
    [Test]
    public async Task VariablesAfterAnObjectDefaultAreStillDefinitions()
    {
        var scan = Scan("""mutation M($in: ThingInput = {name: "x"}, $other: |""");
        string[] expected = ["in", "other"];

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.VariableType);
        await Assert.That(scan.DeclaredVariables).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    // ---- Jump to doc on an argument name ----

    /// <summary>
    /// The argument list never knew the type its field belongs to, so an argument name resolved to
    /// nothing. Only the field case navigated.
    /// </summary>
    [Test]
    [Arguments("{ issues(sta|tes: [OPEN]) { title } }", "states")]
    [Arguments("{ issues(states: [OPEN], filter|By: {}) { title } }", "filterBy")]
    public async Task CtrlClickOnAnArgumentNameNavigatesToIt(string marked, string argument)
    {
        var (text, caret) = Unmark(marked);

        await Assert.That(SchemaReferenceResolver.Resolve(schema, text, caret))
            .IsEqualTo(new SchemaReference("Argument", "Query", "issues", argument));
    }

    [Test]
    public async Task CtrlClickOnAMutationArgumentNamesTheMutationRoot()
    {
        var (text, caret) = Unmark("""mutation { createThing(inp|ut: {name: "x"}) { title } }""");

        await Assert.That(SchemaReferenceResolver.Resolve(schema, text, caret))
            .IsEqualTo(new SchemaReference("Argument", "Mutation", "createThing", "input"));
    }

    /// <summary>The field case, which already worked.</summary>
    [Test]
    public async Task CtrlClickOnAFieldStillNavigatesToIt()
    {
        var (text, caret) = Unmark("{ iss|ues(states: [OPEN]) { title } }");

        await Assert.That(SchemaReferenceResolver.Resolve(schema, text, caret))
            .IsEqualTo(new SchemaReference("Field", "Query", "issues"));
    }

    /// <summary>A directive's argument is not the field's, so it does not navigate as one.</summary>
    [Test]
    public async Task CtrlClickOnADirectiveArgumentIsNotAFieldArgument()
    {
        var (text, caret) = Unmark("query($x: Boolean!) { viewer @include(i|f: $x) { id } }");

        await Assert.That(SchemaReferenceResolver.Resolve(schema, text, caret)).IsNull();
    }
}
