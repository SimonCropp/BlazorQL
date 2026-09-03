/// <summary>
/// The tolerant forward scan that decides what completion may offer. Every document here carries a
/// <c>|</c> where the caret sits, and most of them do not parse — that is the point: the scanner
/// runs mid-edit, so its contract is over brace and paren frames rather than over an AST.
/// </summary>
[TestFixture]
public class ContextScannerTests
{
    static readonly SchemaIndex fixture = LoadFixture();

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
            "directives": []
          }
        }
        """;

    static readonly SchemaIndex roots = Parse(RootsSchema);

    public static SchemaIndex LoadFixture() =>
        Parse(File.ReadAllText(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "DocExplorerTests.schema.json")));

    public static SchemaIndex Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SchemaIndex.Parse(document.RootElement)!;
    }

    /// <summary>Scans <paramref name="marked"/> with the caret at its single <c>|</c>.</summary>
    static ScanResult Scan(string marked, SchemaIndex? schema = null)
    {
        var caret = marked.IndexOf('|');
        Assert.That(caret, Is.GreaterThanOrEqualTo(0), "the document needs a | caret marker");
        return ContextScanner.Scan(schema ?? fixture, marked.Remove(caret, 1), caret);
    }

    [Test]
    public void AnEmptyDocumentOffersTheDocumentLevel()
    {
        var scan = Scan("|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Document));
        Assert.That(scan.CurrentType, Is.Null);
    }

    [Test]
    public void BetweenOperationsIsStillTheDocumentLevel()
    {
        var scan = Scan("{ person { name } }\n\n|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Document));
    }

    [Test]
    public void AnAnonymousSelectionResolvesToTheQueryRoot()
    {
        var scan = Scan("{|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    // The name whose end is the caret is the word being typed, so it must not be read as context —
    // otherwise every keystroke would resolve a different (partial) field.
    [Test]
    public void ThePartialWordAtTheCaretIsNotContext()
    {
        var scan = Scan("{ per|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void ANestedSelectionResolvesThroughTheFieldsType()
    {
        var scan = Scan("{ person { | } }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Person"));
    }

    // friends is [Person]: the wrappers come off before the type is looked up.
    [Test]
    public void AListFieldResolvesToItsUnwrappedType()
    {
        var scan = Scan("{ person { friends { | } } }");

        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Person"));
    }

    [Test]
    public void ASelectionOnAnUnknownFieldResolvesToNoType()
    {
        var scan = Scan("{ nope { | } }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType, Is.Null);
    }

    [Test]
    public void AClosedSelectionPopsBackToItsParent()
    {
        var scan = Scan("{ person { name } | }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void AnInlineFragmentsSelectionResolvesToItsTypeCondition()
    {
        var scan = Scan("{ search { ... on Person { | } } }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Person"));
    }

    [Test]
    public void AFragmentDefinitionsSelectionResolvesToItsTypeCondition()
    {
        var scan = Scan("fragment Fields on Person { | }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Person"));
    }

    [Test]
    public void AnOperationKeywordSelectsItsRootType()
    {
        Assert.That(Scan("query {|", roots).CurrentType!.Name, Is.EqualTo("Query"));
        Assert.That(Scan("mutation {|", roots).CurrentType!.Name, Is.EqualTo("Mutation"));
        Assert.That(Scan("subscription {|", roots).CurrentType!.Name, Is.EqualTo("Subscription"));
    }

    [Test]
    public void ANamedOperationStillSelectsItsRootType()
    {
        var scan = Scan("mutation Save {|", roots);

        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Mutation"));
    }

    /// <summary>
    /// A root the schema does not define resolves to nothing — not to the query root, which is
    /// where the anonymous shorthand goes. Offering Query's fields inside a mutation the schema
    /// cannot serve would be worse than offering none.
    /// </summary>
    [Test]
    public void AnOperationOnARootTheSchemaLacksResolvesToNoType()
    {
        var scan = Scan("mutation {|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType, Is.Null);
    }

    // The anonymous shorthand still reaches the query root.
    [Test]
    public void TheAnonymousShorthandStillFallsBackToTheQueryRoot() =>
        Assert.That(Scan("{|").CurrentType!.Name, Is.EqualTo("Query"));

    [Test]
    public void AnOpenParenAfterAFieldIsAnArgumentName()
    {
        var scan = Scan("{ search(|) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(scan.CurrentField!.Name, Is.EqualTo("search"));
    }

    [Test]
    public void AClosedArgumentListPopsBackToTheSelection()
    {
        var scan = Scan("{ hasArgs(string: \"a\") | }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void AfterAnArgumentColonIsAnArgumentValue()
    {
        var scan = Scan("{ hasArgs(string: |) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.ArgumentValue));
        Assert.That(scan.CurrentArgument!.Name, Is.EqualTo("string"));
    }

    // A literal value consumes the colon, so the next bare name is read as the next argument.
    [Test]
    public void AValueThenACommaReturnsToArgumentNames()
    {
        var scan = Scan("{ pick(color: RED, |) }", roots);

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(scan.CurrentField!.Name, Is.EqualTo("pick"));
    }

    [Test]
    public void AnOpenBraceInAnArgumentValueIsAnInputObject()
    {
        var scan = Scan("{ hasArgs(input: {|}) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.InputField));
        Assert.That(scan.CurrentInputType!.Name, Is.EqualTo("PetInput"));
    }

    [Test]
    public void AfterAnInputFieldColonIsAValueForThatField()
    {
        var scan = Scan("{ hasArgs(input: {name: |}) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.ArgumentValue));
        Assert.That(scan.CurrentArgument!.Name, Is.EqualTo("name"));
        Assert.That(scan.CurrentInputType!.Name, Is.EqualTo("PetInput"));
    }

    // An enum literal consumes the colon, so the next bare name is read as the next input field.
    [Test]
    public void AValueThenACommaReturnsToInputFieldNames()
    {
        var scan = Scan("{ pick(where: {shade: RED, |}) }", roots);

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.InputField));
        Assert.That(scan.CurrentInputType!.Name, Is.EqualTo("Filter"));
    }

    [Test]
    public void AnInputObjectNestsThroughItsOwnFields()
    {
        var scan = Scan("{ pick(where: {nested: {|}}) }", roots);

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.InputField));
        Assert.That(scan.CurrentInputType!.Name, Is.EqualTo("Filter"));
    }

    [Test]
    public void ABracketIsAValuePositionForTheEnclosingArgument()
    {
        var scan = Scan("{ pick(tags: [|]) }", roots);

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.ArgumentValue));
        Assert.That(scan.CurrentArgument!.Name, Is.EqualTo("tags"));
    }

    // A list of input objects: the bracket carries the argument through to the brace.
    [Test]
    public void AnInputObjectInsideAListStillResolves()
    {
        var scan = Scan("{ hasArgs(input: [{|}]) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.InputField));
        Assert.That(scan.CurrentInputType!.Name, Is.EqualTo("PetInput"));
    }

    /// <summary>
    /// Every shape of finished value has to close the value the colon opened, or the next argument
    /// position offers values where names belong. A bare name clears the flag where it is consumed;
    /// the rest are cleared as the literal ends.
    /// </summary>
    [Test]
    public void AClosedLiteralValueReturnsToArgumentNames()
    {
        Assert.That(Scan("{ pick(tags: [\"a\"], |) }", roots).Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(Scan("{ hasArgs(string: \"a\", |) }").Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(Scan("{ hasArgs(count: 1, |) }").Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(Scan("{ hasArgs(count: -1.5e3, |) }").Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(Scan("{ hasArgs(input: {name: \"a\"}, |) }").Mode, Is.EqualTo(ScanMode.ArgumentName));
        Assert.That(Scan("query Q($s: String) { hasArgs(string: $s, |) }").Mode, Is.EqualTo(ScanMode.ArgumentName));
    }

    // The same inside an input object, where the next position is an input field name.
    [Test]
    public void AClosedLiteralValueReturnsToInputFieldNames()
    {
        var scan = Scan("{ hasArgs(input: {name: \"a\", |}) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.InputField));
        Assert.That(scan.CurrentInputType!.Name, Is.EqualTo("PetInput"));
    }

    // A literal still being typed is not a finished value.
    [Test]
    public void ALiteralAtTheCaretIsStillAValuePosition()
    {
        Assert.That(Scan("{ hasArgs(count: 1|").Mode, Is.EqualTo(ScanMode.ArgumentValue));
        Assert.That(Scan("{ hasArgs(string: \"a|").Mode, Is.EqualTo(ScanMode.ArgumentValue));
    }

    [Test]
    public void AVariableTypeFollowsTheColonInADefinition()
    {
        var scan = Scan("query Q($id: |)");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.VariableType));
    }

    [Test]
    public void DefinedVariablesAreCollectedInOrder()
    {
        var scan = Scan("query Q($a: String, $b: |)");
        string[] expected = ["a", "b"];

        Assert.That(scan.DeclaredVariables, Is.EqualTo(expected));
    }

    // Nothing to offer where the variable's own name is being typed.
    [Test]
    public void AVariableNamePositionOffersNothing()
    {
        var scan = Scan("query Q($|)");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.None));
    }

    [Test]
    public void ADollarInAnArgumentValueIsAVariableReference()
    {
        var scan = Scan("query Q($term: String) { search(term: $|) }");

        string[] expected = ["term"];

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Variable));
        Assert.That(scan.DeclaredVariables, Is.EqualTo(expected));
    }

    [Test]
    public void ADollarInAnInputObjectIsAVariableReference()
    {
        var scan = Scan("query Q($n: String) { hasArgs(input: {name: $|}) }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Variable));
    }

    [Test]
    public void AnEllipsisIsAFragmentSpread()
    {
        var scan = Scan("{ ...|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.FragmentSpread));
    }

    [Test]
    public void AnEllipsisFollowedByOnIsATypeCondition()
    {
        var scan = Scan("{ ... on |");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.TypeCondition));
    }

    [Test]
    public void AFragmentDefinitionsOnIsATypeCondition()
    {
        var scan = Scan("fragment Fields on |");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.TypeCondition));
    }

    // Mid-edit, the ellipsis is often not there yet: a bare "on" opening a selection still reads
    // as a type condition rather than as a field named on.
    [Test]
    public void ABareOnInsideASelectionIsATypeCondition()
    {
        var scan = Scan("{ person { on |");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.TypeCondition));
    }

    [Test]
    public void AnAtSignIsADirective()
    {
        var scan = Scan("{ person @|");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Directive));
    }

    // A directive already named is structurally inert — the selection carries on around it.
    [Test]
    public void ANamedDirectiveLeavesTheSelectionIntact()
    {
        var scan = Scan("{ person @include(if: true) { | } }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Person"));
    }

    // Fragment names come from the whole document, including the part after the caret.
    [Test]
    public void FragmentNamesAreCollectedFromTheWholeDocument()
    {
        var scan = Scan("{ ...| }\n\nfragment Fields on Person { name }\nfragment More on Post { title }");

        string[] expected = ["Fields", "More"];

        Assert.That(scan.FragmentNames, Is.EqualTo(expected));
    }

    [Test]
    public void ACommentIsSkippedWhole()
    {
        var scan = Scan("{\n  # } person { nonsense\n  |\n}");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void AStringIsSkippedWhole()
    {
        var scan = Scan("{ hasArgs(string: \"} # { ) $\") | }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void AnEscapedQuoteDoesNotEndTheString()
    {
        var scan = Scan("{ hasArgs(string: \"a\\\" } {\") | }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void ABlockStringIsSkippedWhole()
    {
        var scan = Scan("{ hasArgs(string: \"\"\"\n } { )\n\"\"\") | }");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    // An unterminated block string swallows the rest of the document rather than derailing the
    // frames it has already resolved.
    [Test]
    public void AnUnterminatedBlockStringLeavesTheFramesAlone()
    {
        var scan = Scan("{ person { name } \"\"\"unclosed |");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Query"));
    }

    [Test]
    public void AnUnterminatedStringDoesNotRunPastTheCaret()
    {
        var scan = Scan("{ hasArgs(string: \"unclosed |");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.ArgumentValue));
        Assert.That(scan.CurrentArgument!.Name, Is.EqualTo("string"));
    }

    [Test]
    public void AnOffsetPastTheEndIsClamped()
    {
        var scan = ContextScanner.Scan(fixture, "{ person { ", 500);

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Selection));
        Assert.That(scan.CurrentType!.Name, Is.EqualTo("Person"));
    }

    [Test]
    public void ANegativeOffsetIsClamped()
    {
        var scan = ContextScanner.Scan(fixture, "{ person { name } }", -5);

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Document));
    }

    // More closers than openers is ordinary mid-edit text and must not throw.
    [Test]
    public void UnbalancedClosersAreIgnored()
    {
        var scan = Scan("} ) ] |");

        Assert.That(scan.Mode, Is.EqualTo(ScanMode.Document));
    }
}
