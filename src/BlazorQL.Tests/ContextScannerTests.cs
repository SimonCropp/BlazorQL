/// <summary>
/// The tolerant forward scan that decides what completion may offer. Every document here carries a
/// <c>|</c> where the caret sits, and most of them do not parse — that is the point: the scanner
/// runs mid-edit, so its contract is over brace and paren frames rather than over an AST.
/// </summary>
public class ContextScannerTests
{
    readonly SchemaIndex fixture = LoadFixture();

    /// <summary>
    /// The shared doc-explorer fixture has no mutation or subscription root, no enum-typed or
    /// boolean argument and no nested input object, so the frames those reach need a schema of
    /// their own. <c>__Type</c> is here because a real introspection result carries the
    /// introspection types, and completion has to leave them out.
    /// </summary>
    public const string RootsSchema =
        """
        {
          "__schema": {
            "queryType": {"name": "Query"},
            "mutationType": {"name": "Mutation"},
            "subscriptionType": {"name": "Subscription"},
            "types": [
              {"kind": "OBJECT", "name": "Query", "fields": [
                {"name": "pick", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false, "args": [
                  {"name": "color", "type": {"kind": "ENUM", "name": "Color"}, "isDeprecated": false},
                  {"name": "flag", "type": {"kind": "SCALAR", "name": "Boolean"}, "isDeprecated": false},
                  {"name": "where", "type": {"kind": "INPUT_OBJECT", "name": "Filter"}, "isDeprecated": false},
                  {"name": "tags", "type": {"kind": "LIST", "ofType": {"kind": "SCALAR", "name": "String"}}, "isDeprecated": false}
                ]}
              ]},
              {"kind": "OBJECT", "name": "Mutation", "fields": [
                {"name": "save", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false, "args": []}
              ]},
              {"kind": "OBJECT", "name": "Subscription", "fields": [
                {"name": "ticks", "type": {"kind": "SCALAR", "name": "Int"}, "isDeprecated": false, "args": []}
              ]},
              {"kind": "INPUT_OBJECT", "name": "Filter", "inputFields": [
                {"name": "shade", "type": {"kind": "ENUM", "name": "Color"}, "isDeprecated": false},
                {"name": "nested", "type": {"kind": "INPUT_OBJECT", "name": "Filter"}, "isDeprecated": false}
              ]},
              {"kind": "ENUM", "name": "Color", "enumValues": [
                {"name": "RED", "isDeprecated": false},
                {"name": "GREEN", "isDeprecated": true, "deprecationReason": "Faded."}
              ]},
              {"kind": "OBJECT", "name": "__Type", "fields": []},
              {"kind": "SCALAR", "name": "String"},
              {"kind": "SCALAR", "name": "Int"},
              {"kind": "SCALAR", "name": "Boolean"}
            ],
            "directives": [
              {"name": "include", "locations": ["FIELD"], "args": [
                {"name": "if", "type": {"kind": "NON_NULL", "ofType": {"kind": "SCALAR", "name": "Boolean"}}, "isDeprecated": false}
              ]},
              {"name": "size", "locations": ["FIELD"], "args": [
                {"name": "width", "type": {"kind": "SCALAR", "name": "Int"}, "isDeprecated": false},
                {"name": "shade", "type": {"kind": "ENUM", "name": "Color"}, "isDeprecated": false}
              ]}
            ]
          }
        }
        """;

    readonly SchemaIndex roots = Parse(RootsSchema);

    /// <summary>
    /// A new index on every call, held in instance fields so that each test gets its own. Tests
    /// run in parallel, and a <see cref="SchemaIndex"/> shared between them races as it fills its
    /// lookup tables.
    /// </summary>
    public static SchemaIndex LoadFixture() =>
        Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, ProjectFiles.DocExplorerTests_schema_json)));

    public static SchemaIndex Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SchemaIndex.Parse(document.RootElement)!;
    }

    /// <summary>Scans <paramref name="marked"/> with the caret at its single <c>|</c>.</summary>
    ScanResult Scan(string marked, SchemaIndex? schema = null)
    {
        var caret = marked.IndexOf('|');
        if (caret < 0)
        {
            throw new ArgumentException("the document needs a | caret marker", nameof(marked));
        }

        return ContextScanner.Scan(schema ?? fixture, marked.Remove(caret, 1), caret);
    }

    [Test]
    public async Task AnEmptyDocumentOffersTheDocumentLevel()
    {
        var scan = Scan("|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Document);
        await Assert.That(scan.CurrentType).IsNull();
    }

    [Test]
    public async Task BetweenOperationsIsStillTheDocumentLevel()
    {
        var scan = Scan("{ person { name } }\n\n|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Document);
    }

    [Test]
    public async Task AnAnonymousSelectionResolvesToTheQueryRoot()
    {
        var scan = Scan("{|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    // The name whose end is the caret is the word being typed, so it must not be read as context —
    // otherwise every keystroke would resolve a different (partial) field.
    [Test]
    public async Task ThePartialWordAtTheCaretIsNotContext()
    {
        var scan = Scan("{ per|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task ANestedSelectionResolvesThroughTheFieldsType()
    {
        var scan = Scan("{ person { | } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Person");
    }

    // friends is [Person]: the wrappers come off before the type is looked up.
    [Test]
    public async Task AListFieldResolvesToItsUnwrappedType()
    {
        var scan = Scan("{ person { friends { | } } }");

        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Person");
    }

    [Test]
    public async Task ASelectionOnAnUnknownFieldResolvesToNoType()
    {
        var scan = Scan("{ nope { | } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType).IsNull();
    }

    [Test]
    public async Task AClosedSelectionPopsBackToItsParent()
    {
        var scan = Scan("{ person { name } | }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task AnInlineFragmentsSelectionResolvesToItsTypeCondition()
    {
        var scan = Scan("{ search { ... on Person { | } } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Person");
    }

    [Test]
    public async Task AFragmentDefinitionsSelectionResolvesToItsTypeCondition()
    {
        var scan = Scan("fragment Fields on Person { | }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Person");
    }

    [Test]
    public async Task AnOperationKeywordSelectsItsRootType()
    {
        await Assert.That(Scan("query {|", roots).CurrentType!.Name).IsEqualTo("Query");
        await Assert.That(Scan("mutation {|", roots).CurrentType!.Name).IsEqualTo("Mutation");
        await Assert.That(Scan("subscription {|", roots).CurrentType!.Name).IsEqualTo("Subscription");
    }

    [Test]
    public async Task ANamedOperationStillSelectsItsRootType()
    {
        var scan = Scan("mutation Save {|", roots);

        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Mutation");
    }

    /// <summary>
    /// A root the schema does not define resolves to nothing — not to the query root, which is
    /// where the anonymous shorthand goes. Offering Query's fields inside a mutation the schema
    /// cannot serve would be worse than offering none.
    /// </summary>
    [Test]
    public async Task AnOperationOnARootTheSchemaLacksResolvesToNoType()
    {
        var scan = Scan("mutation {|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType).IsNull();
    }

    // The anonymous shorthand still reaches the query root.
    [Test]
    public async Task TheAnonymousShorthandStillFallsBackToTheQueryRoot() =>
        await Assert.That(Scan("{|").CurrentType!.Name).IsEqualTo("Query");

    [Test]
    public async Task AnOpenParenAfterAFieldIsAnArgumentName()
    {
        var scan = Scan("{ search(|) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(scan.CurrentField!.Name).IsEqualTo("search");
    }

    [Test]
    public async Task AClosedArgumentListPopsBackToTheSelection()
    {
        var scan = Scan("{ hasArgs(string: \"a\") | }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task AfterAnArgumentColonIsAnArgumentValue()
    {
        var scan = Scan("{ hasArgs(string: |) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentValue);
        await Assert.That(scan.CurrentArgument!.Name).IsEqualTo("string");
    }

    // A literal value consumes the colon, so the next bare name is read as the next argument.
    [Test]
    public async Task AValueThenACommaReturnsToArgumentNames()
    {
        var scan = Scan("{ pick(color: RED, |) }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(scan.CurrentField!.Name).IsEqualTo("pick");
    }

    [Test]
    public async Task AnOpenBraceInAnArgumentValueIsAnInputObject()
    {
        var scan = Scan("{ hasArgs(input: {|}) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("PetInput");
    }

    [Test]
    public async Task AfterAnInputFieldColonIsAValueForThatField()
    {
        var scan = Scan("{ hasArgs(input: {name: |}) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentValue);
        await Assert.That(scan.CurrentArgument!.Name).IsEqualTo("name");
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("PetInput");
    }

    // An enum literal consumes the colon, so the next bare name is read as the next input field.
    [Test]
    public async Task AValueThenACommaReturnsToInputFieldNames()
    {
        var scan = Scan("{ pick(where: {shade: RED, |}) }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("Filter");
    }

    [Test]
    public async Task AnInputObjectNestsThroughItsOwnFields()
    {
        var scan = Scan("{ pick(where: {nested: {|}}) }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("Filter");
    }

    [Test]
    public async Task ABracketIsAValuePositionForTheEnclosingArgument()
    {
        var scan = Scan("{ pick(tags: [|]) }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentValue);
        await Assert.That(scan.CurrentArgument!.Name).IsEqualTo("tags");
    }

    // A list of input objects: the bracket carries the argument through to the brace.
    [Test]
    public async Task AnInputObjectInsideAListStillResolves()
    {
        var scan = Scan("{ hasArgs(input: [{|}]) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("PetInput");
    }

    /// <summary>
    /// Every shape of finished value has to close the value the colon opened, or the next argument
    /// position offers values where names belong. A bare name clears the flag where it is consumed;
    /// the rest are cleared as the literal ends.
    /// </summary>
    [Test]
    public async Task AClosedLiteralValueReturnsToArgumentNames()
    {
        await Assert.That(Scan("{ pick(tags: [\"a\"], |) }", roots).Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(Scan("{ hasArgs(string: \"a\", |) }").Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(Scan("{ hasArgs(count: 1, |) }").Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(Scan("{ hasArgs(count: -1.5e3, |) }").Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(Scan("{ hasArgs(input: {name: \"a\"}, |) }").Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(Scan("query Q($s: String) { hasArgs(string: $s, |) }").Mode).IsEqualTo(ScanMode.ArgumentName);
    }

    // The same inside an input object, where the next position is an input field name.
    [Test]
    public async Task AClosedLiteralValueReturnsToInputFieldNames()
    {
        var scan = Scan("{ hasArgs(input: {name: \"a\", |}) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.InputField);
        await Assert.That(scan.CurrentInputType!.Name).IsEqualTo("PetInput");
    }

    // A literal still being typed is not a finished value.
    [Test]
    public async Task ALiteralAtTheCaretIsStillAValuePosition()
    {
        await Assert.That(Scan("{ hasArgs(count: 1|").Mode).IsEqualTo(ScanMode.ArgumentValue);
        await Assert.That(Scan("{ hasArgs(string: \"a|").Mode).IsEqualTo(ScanMode.ArgumentValue);
    }

    [Test]
    public async Task AVariableTypeFollowsTheColonInADefinition()
    {
        var scan = Scan("query Q($id: |)");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.VariableType);
    }

    [Test]
    public async Task DefinedVariablesAreCollectedInOrder()
    {
        var scan = Scan("query Q($a: String, $b: |)");
        string[] expected = ["a", "b"];

        await Assert.That(scan.DeclaredVariables).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    // Nothing to offer where the variable's own name is being typed.
    [Test]
    public async Task AVariableNamePositionOffersNothing()
    {
        var scan = Scan("query Q($|)");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.None);
    }

    [Test]
    public async Task ADollarInAnArgumentValueIsAVariableReference()
    {
        var scan = Scan("query Q($term: String) { search(term: $|) }");

        string[] expected = ["term"];

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Variable);
        await Assert.That(scan.DeclaredVariables).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ADollarInAnInputObjectIsAVariableReference()
    {
        var scan = Scan("query Q($n: String) { hasArgs(input: {name: $|}) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Variable);
    }

    [Test]
    public async Task AnEllipsisIsAFragmentSpread()
    {
        var scan = Scan("{ ...|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.FragmentSpread);
    }

    [Test]
    public async Task AnEllipsisFollowedByOnIsATypeCondition()
    {
        var scan = Scan("{ ... on |");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.TypeCondition);
    }

    [Test]
    public async Task AFragmentDefinitionsOnIsATypeCondition()
    {
        var scan = Scan("fragment Fields on |");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.TypeCondition);
    }

    // Mid-edit, the ellipsis is often not there yet: a bare "on" opening a selection still reads
    // as a type condition rather than as a field named on.
    [Test]
    public async Task ABareOnInsideASelectionIsATypeCondition()
    {
        var scan = Scan("{ person { on |");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.TypeCondition);
    }

    [Test]
    public async Task AnAtSignIsADirective()
    {
        var scan = Scan("{ person @|");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Directive);
    }

    // A directive already named is structurally inert — the selection carries on around it.
    [Test]
    public async Task ANamedDirectiveLeavesTheSelectionIntact()
    {
        var scan = Scan("{ person @include(if: true) { | } }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Person");
    }

    // Fragment names come from the whole document, including the part after the caret.
    [Test]
    public async Task FragmentNamesAreCollectedFromTheWholeDocument()
    {
        var scan = Scan("{ ...| }\n\nfragment Fields on Person { name }\nfragment More on Post { title }");

        string[] expected = ["Fields", "More"];

        await Assert.That(scan.FragmentNames).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ACommentIsSkippedWhole()
    {
        var scan = Scan("{\n  # } person { nonsense\n  |\n}");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task AStringIsSkippedWhole()
    {
        var scan = Scan("{ hasArgs(string: \"} # { ) $\") | }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task AnEscapedQuoteDoesNotEndTheString()
    {
        var scan = Scan("{ hasArgs(string: \"a\\\" } {\") | }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task ABlockStringIsSkippedWhole()
    {
        var scan = Scan("{ hasArgs(string: \"\"\"\n } { )\n\"\"\") | }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    // An unterminated block string swallows the rest of the document rather than derailing the
    // frames it has already resolved.
    [Test]
    public async Task AnUnterminatedBlockStringLeavesTheFramesAlone()
    {
        var scan = Scan("{ person { name } \"\"\"unclosed |");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Query");
    }

    [Test]
    public async Task AnUnterminatedStringDoesNotRunPastTheCaret()
    {
        var scan = Scan("{ hasArgs(string: \"unclosed |");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentValue);
        await Assert.That(scan.CurrentArgument!.Name).IsEqualTo("string");
    }

    [Test]
    public async Task AnOffsetPastTheEndIsClamped()
    {
        var scan = ContextScanner.Scan(fixture, "{ person { ", 500);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);
        await Assert.That(scan.CurrentType!.Name).IsEqualTo("Person");
    }

    [Test]
    public async Task ANegativeOffsetIsClamped()
    {
        var scan = ContextScanner.Scan(fixture, "{ person { name } }", -5);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Document);
    }

    // More closers than openers is ordinary mid-edit text and must not throw.
    [Test]
    public async Task UnbalancedClosersAreIgnored()
    {
        var scan = Scan("} ) ] |");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Document);
    }

    // ---- Directive arguments ----

    // The "(" after a directive name opens the directive's argument list. Before this was tracked
    // the frame carried the enclosing field, so the field's arguments were what got offered.
    [Test]
    public async Task AnOpenParenAfterADirectiveIsTheDirectivesArgumentList()
    {
        var scan = Scan("{ pick @size(|) }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(scan.CurrentDirective!.Name).IsEqualTo("size");
        await Assert.That(scan.CurrentField).IsNull();
    }

    [Test]
    public async Task ADirectiveArgumentValueResolvesAgainstTheDirectivesArgument()
    {
        var scan = Scan("{ pick @size(shade: |) }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentValue);
        await Assert.That(scan.CurrentDirective!.Name).IsEqualTo("size");
        await Assert.That(scan.CurrentArgument!.Name).IsEqualTo("shade");
    }

    // The everyday case: a directive on a field that has arguments of its own.
    [Test]
    public async Task ADirectiveOnAFieldWithArgumentsDoesNotOfferTheFieldsArguments()
    {
        var scan = Scan("{ hasArgs @repeat(|) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(scan.CurrentDirective!.Name).IsEqualTo("repeat");
        await Assert.That(scan.CurrentField).IsNull();
    }

    [Test]
    public async Task AnUnknownDirectiveOffersNoArguments()
    {
        var scan = Scan("{ hasArgs @nope(|) }");

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(scan.CurrentDirective).IsNull();
        await Assert.That(scan.CurrentField).IsNull();
    }

    // A closed directive argument list leaves the field's own list reachable again.
    [Test]
    public async Task TheFieldsArgumentsAreStillReachableAfterADirective()
    {
        var scan = Scan("{ pick @size(width: 1) | }", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.Selection);

        var arguments = Scan("{ pick(|) @size(width: 1) }", roots);

        await Assert.That(arguments.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(arguments.CurrentField!.Name).IsEqualTo("pick");
        await Assert.That(arguments.CurrentDirective).IsNull();
    }

    [Test]
    public async Task ADirectiveOnAnOperationOpensItsArgumentsRatherThanVariableDefinitions()
    {
        var scan = Scan("query Q @size(|)", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.ArgumentName);
        await Assert.That(scan.CurrentDirective!.Name).IsEqualTo("size");
    }

    [Test]
    public async Task VariableDefinitionsStillOpenAfterAnOperationName()
    {
        var scan = Scan("query Q($v: |)", roots);

        await Assert.That(scan.Mode).IsEqualTo(ScanMode.VariableType);
    }

    // ---- Fragment name collection ----

    // "fragment" is only the keyword when it stands as a word of its own. A comment that mentions
    // fragments used to yield a fragment named "s", and { fragmentCount } one named "Count".
    [Test]
    public async Task ProseMentioningFragmentsDefinesNoFragment()
    {
        var scan = Scan("{ ...| # see fragments here\n }");

        await Assert.That(scan.FragmentNames).IsEmpty();
    }

    [Test]
    public async Task AFieldWhoseNameStartsWithFragmentDefinesNoFragment()
    {
        var scan = Scan("{ fragmentCount ...| }");

        await Assert.That(scan.FragmentNames).IsEmpty();
    }

    [Test]
    public async Task AStringMentioningFragmentDefinesNoFragment()
    {
        var scan = Scan("""{ search(term: "fragment Nope on Person") ...| }""");

        await Assert.That(scan.FragmentNames).IsEmpty();
    }

    [Test]
    public async Task ABlockStringMentioningFragmentDefinesNoFragment()
    {
        var scan = Scan(
            """"
            { search(term: """
            fragment Nope on Person
            """) ...| }
            """");

        await Assert.That(scan.FragmentNames).IsEmpty();
    }

    [Test]
    public async Task AFragmentWithNoNameDefinesNothing()
    {
        var scan = Scan("{ ...| }\n\nfragment ");

        await Assert.That(scan.FragmentNames).IsEmpty();
    }

    // The real thing still lands, next to the near misses.
    [Test]
    public async Task RealDefinitionsSurviveAlongsideTheNearMisses()
    {
        var scan = Scan(
            """
            # a comment about fragments
            { fragmentCount ...| }

            fragment Fields on Person { name }
            """);

        string[] expected = ["Fields"];

        await Assert.That(scan.FragmentNames).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}