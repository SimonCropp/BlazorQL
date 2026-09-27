/// <summary>
/// What each scan mode turns into, over the same <c>|</c> caret convention
/// <see cref="ContextScannerTests"/> uses. The scanner decides the mode; these tests are about the
/// entries — their kinds, details, deprecation and the sort text that holds declaration order
/// against Monaco's alphabetical default.
/// </summary>
public class CompletionEngineTests
{
    static readonly SchemaIndex fixture = ContextScannerTests.LoadFixture();
    static readonly SchemaIndex roots = ContextScannerTests.Parse(ContextScannerTests.RootsSchema);

    static IReadOnlyList<CompletionEntry> Complete(string marked, SchemaIndex? schema = null)
    {
        var caret = marked.IndexOf('|');
        if (caret < 0)
        {
            throw new ArgumentException("the document needs a | caret marker", nameof(marked));
        }

        return CompletionEngine.Complete(schema ?? fixture, marked.Remove(caret, 1), caret);
    }

    static string[] Labels(string marked, SchemaIndex? schema = null) =>
        [.. Complete(marked, schema).Select(_ => _.Label)];

    static string[] Kinds(string marked, SchemaIndex? schema = null) =>
        [.. Complete(marked, schema).Select(_ => _.Kind)];

    /// <summary>One line per entry, in the order the engine produced them.</summary>
    static string Render(IReadOnlyList<CompletionEntry> entries) =>
        string.Join(
            "\n",
            entries.Select(_ => string.Join(
                " | ",
                _.SortText,
                _.Kind,
                _.Label,
                _.InsertText ?? "-",
                _.Detail ?? "-",
                _.Deprecated ? "deprecated" : "-",
                _.Documentation?.ReplaceLineEndings(" ") ?? "-")));

    [Test]
    public async Task TheDocumentLevelOffersTheOperationKeywords()
    {
        string[] expected = ["query", "mutation", "subscription", "fragment", "{"];

        await Assert.That(Labels("|")).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(Kinds("|")).All(_ => _ == "Keyword");
    }

    // The root query type also carries the introspection meta-fields; every other type does not.
    [Test]
    public Task ASelectionOnTheQueryRootOffersItsFieldsAndTheMetaFields() =>
        Verify(Render(Complete("{ | }")));

    [Test]
    public Task ASelectionOnAnObjectOffersItsFieldsAndTypename() =>
        Verify(Render(Complete("{ person { | } }")));

    // A union has no fields of its own, so only __typename is reachable directly on it.
    [Test]
    public async Task ASelectionOnAUnionOffersOnlyTypename()
    {
        string[] expected = ["__typename"];

        await Assert.That(Labels("{ search { | } }")).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ASelectionOnAnUnknownTypeOffersNothing() =>
        await Assert.That(Complete("{ nope { | } }")).IsEmpty();

    [Test]
    public Task AnArgumentListOffersTheFieldsArguments() =>
        Verify(Render(Complete("{ hasArgs(|) }")));

    [Test]
    public async Task AFieldWithoutArgumentsOffersNothing() =>
        await Assert.That(Complete("{ person(|) }")).IsEmpty();

    [Test]
    public async Task AnEnumArgumentOffersItsValues()
    {
        var entries = Complete("{ pick(color: |) }", roots);
        string[] expected = ["RED", "GREEN"];

        await Assert.That(Labels("{ pick(color: |) }", roots)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(entries.Select(_ => _.Kind)).All(_ => _ == "EnumMember");
        await Assert.That(entries.Select(_ => _.Detail)).All(_ => _ == "Color");
        await Assert.That(entries.Single(_ => _.Label == "GREEN").Deprecated).IsTrue();
    }

    [Test]
    public async Task ABooleanArgumentOffersTrueAndFalse()
    {
        string[] expected = ["true", "false"];

        await Assert.That(Labels("{ pick(flag: |) }", roots)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(Kinds("{ pick(flag: |) }", roots)).All(_ => _ == "Value");
    }

    // Declared variables are offered beside the literals, dollar included so the insert is usable.
    [Test]
    public async Task DeclaredVariablesAreOfferedAsArgumentValues()
    {
        const string document = "query Q($shade: Color) { pick(color: |) }";
        var entries = Complete(document, roots);
        string[] expected = ["RED", "GREEN", "$shade"];

        await Assert.That(Labels(document, roots)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(entries[^1].Kind).IsEqualTo("Variable");
    }

    // A scalar the engine has no literals for, and nothing declared to reference.
    [Test]
    public async Task AStringArgumentWithNoVariablesOffersNothing() =>
        await Assert.That(Complete("{ hasArgs(string: |) }")).IsEmpty();

    [Test]
    public Task AnInputObjectOffersItsInputFields() =>
        Verify(Render(Complete("{ hasArgs(input: {|}) }")));

    // A brace where the argument is a scalar is nonsense the schema cannot fill in.
    [Test]
    public async Task ABraceOnAnArgumentThatIsNotAnInputObjectOffersNothing() =>
        await Assert.That(Complete("{ hasArgs(string: {|}) }")).IsEmpty();

    [Test]
    public async Task AVariableReferenceOffersTheDeclaredNamesWithoutTheirDollar()
    {
        const string document = "query Q($a: String, $b: Int) { search(term: $|) }";
        string[] expected = ["a", "b"];

        await Assert.That(Labels(document)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(Kinds(document)).All(_ => _ == "Variable");
    }

    [Test]
    public async Task ATypeConditionOffersTheCompositeTypes()
    {
        string[] expected = ["Query", "Person", "Named", "SearchResult", "Post"];

        await Assert.That(Labels("{ ... on |")).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AVariableTypeOffersTheInputTypes()
    {
        string[] expected = ["Color", "PetInput", "String", "Int", "JSON"];

        await Assert.That(Labels("query Q($x: |)")).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    // Introspection types are part of every real schema and belong in no completion list.
    [Test]
    public async Task TheIntrospectionTypesAreLeftOut()
    {
        string[] expected = ["Query", "Mutation", "Subscription"];

        await Assert.That(Labels("{ ... on |", roots)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task ADirectivePositionOffersTheSchemasDirectives()
    {
        var entries = Complete("{ person @|");
        string[] expected = ["repeat"];

        await Assert.That(Labels("{ person @|")).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(entries[0].Kind).IsEqualTo("Interface");
        await Assert.That(entries[0].Detail).IsEqualTo("directive");
    }

    // Named spreads first, then "on Type" for the inline form — sorted after them by the z prefix.
    [Test]
    public Task AFragmentSpreadOffersTheNamedFragmentsThenTheInlineForm() =>
        Verify(Render(Complete("{ ...| }\n\nfragment Fields on Person { name }")));

    [Test]
    public async Task AModeWithNothingToOfferReturnsAnEmptyList() =>
        await Assert.That(Complete("query Q($|)")).IsEmpty();

    // Monaco sorts by SortText, so declaration order only survives if the prefix is ascending.
    [Test]
    public async Task SortTextKeepsDeclarationOrder()
    {
        var sortTexts = Complete("{ | }").Select(_ => _.SortText).ToList();

        await Assert.That(sortTexts).IsEquivalentTo(sortTexts.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        await Assert.That(sortTexts[0]).IsEqualTo("0000person");
    }

    // ---- Directive arguments ----

    [Test]
    public async Task ADirectiveArgumentListOffersTheDirectivesArguments()
    {
        string[] expected = ["width", "shade"];

        await Assert.That(Labels("{ pick @size(|", roots)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    // The everyday case: @include on a field that has arguments of its own. The field's are not
    // what a directive's parentheses are asking for.
    [Test]
    public async Task ADirectiveOnAFieldWithArgumentsDoesNotOfferTheFieldsArguments() =>
        await Assert.That(Labels("{ hasArgs @repeat(|")).IsEmpty();

    [Test]
    public async Task AnUnknownDirectiveOffersNothing() =>
        await Assert.That(Labels("{ hasArgs @nope(|")).IsEmpty();

    [Test]
    public async Task ADirectiveArgumentValueOffersTheArgumentsType()
    {
        string[] expected = ["RED", "GREEN"];

        await Assert.That(Labels("{ pick @size(shade: |", roots)).IsEquivalentTo(expected, CollectionOrdering.Matching);
    }
}