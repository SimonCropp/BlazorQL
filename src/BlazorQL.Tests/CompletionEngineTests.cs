/// <summary>
/// What each scan mode turns into, over the same <c>|</c> caret convention
/// <see cref="ContextScannerTests"/> uses. The scanner decides the mode; these tests are about the
/// entries — their kinds, details, deprecation and the sort text that holds declaration order
/// against Monaco's alphabetical default.
/// </summary>
[TestFixture]
public class CompletionEngineTests
{
    static readonly SchemaIndex fixture = ContextScannerTests.LoadFixture();
    static readonly SchemaIndex roots = ContextScannerTests.Parse(ContextScannerTests.RootsSchema);

    static IReadOnlyList<CompletionEntry> Complete(string marked, SchemaIndex? schema = null)
    {
        var caret = marked.IndexOf('|');
        Assert.That(caret, Is.GreaterThanOrEqualTo(0), "the document needs a | caret marker");
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
    public void TheDocumentLevelOffersTheOperationKeywords()
    {
        string[] expected = ["query", "mutation", "subscription", "fragment", "{"];

        Assert.That(Labels("|"), Is.EqualTo(expected));
        Assert.That(Kinds("|"), Is.All.EqualTo("Keyword"));
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
    public void ASelectionOnAUnionOffersOnlyTypename()
    {
        string[] expected = ["__typename"];

        Assert.That(Labels("{ search { | } }"), Is.EqualTo(expected));
    }

    [Test]
    public void ASelectionOnAnUnknownTypeOffersNothing() =>
        Assert.That(Complete("{ nope { | } }"), Is.Empty);

    [Test]
    public Task AnArgumentListOffersTheFieldsArguments() =>
        Verify(Render(Complete("{ hasArgs(|) }")));

    [Test]
    public void AFieldWithoutArgumentsOffersNothing() =>
        Assert.That(Complete("{ person(|) }"), Is.Empty);

    [Test]
    public void AnEnumArgumentOffersItsValues()
    {
        var entries = Complete("{ pick(color: |) }", roots);
        string[] expected = ["RED", "GREEN"];

        Assert.That(Labels("{ pick(color: |) }", roots), Is.EqualTo(expected));
        Assert.That(entries.Select(_ => _.Kind), Is.All.EqualTo("EnumMember"));
        Assert.That(entries.Select(_ => _.Detail), Is.All.EqualTo("Color"));
        Assert.That(entries.Single(_ => _.Label == "GREEN").Deprecated, Is.True);
    }

    [Test]
    public void ABooleanArgumentOffersTrueAndFalse()
    {
        string[] expected = ["true", "false"];

        Assert.That(Labels("{ pick(flag: |) }", roots), Is.EqualTo(expected));
        Assert.That(Kinds("{ pick(flag: |) }", roots), Is.All.EqualTo("Value"));
    }

    // Declared variables are offered beside the literals, dollar included so the insert is usable.
    [Test]
    public void DeclaredVariablesAreOfferedAsArgumentValues()
    {
        const string document = "query Q($shade: Color) { pick(color: |) }";
        var entries = Complete(document, roots);
        string[] expected = ["RED", "GREEN", "$shade"];

        Assert.That(Labels(document, roots), Is.EqualTo(expected));
        Assert.That(entries[^1].Kind, Is.EqualTo("Variable"));
    }

    // A scalar the engine has no literals for, and nothing declared to reference.
    [Test]
    public void AStringArgumentWithNoVariablesOffersNothing() =>
        Assert.That(Complete("{ hasArgs(string: |) }"), Is.Empty);

    [Test]
    public Task AnInputObjectOffersItsInputFields() =>
        Verify(Render(Complete("{ hasArgs(input: {|}) }")));

    // A brace where the argument is a scalar is nonsense the schema cannot fill in.
    [Test]
    public void ABraceOnAnArgumentThatIsNotAnInputObjectOffersNothing() =>
        Assert.That(Complete("{ hasArgs(string: {|}) }"), Is.Empty);

    [Test]
    public void AVariableReferenceOffersTheDeclaredNamesWithoutTheirDollar()
    {
        const string document = "query Q($a: String, $b: Int) { search(term: $|) }";
        string[] expected = ["a", "b"];

        Assert.That(Labels(document), Is.EqualTo(expected));
        Assert.That(Kinds(document), Is.All.EqualTo("Variable"));
    }

    [Test]
    public void ATypeConditionOffersTheCompositeTypes()
    {
        string[] expected = ["Query", "Person", "Named", "SearchResult", "Post"];

        Assert.That(Labels("{ ... on |"), Is.EqualTo(expected));
    }

    [Test]
    public void AVariableTypeOffersTheInputTypes()
    {
        string[] expected = ["Color", "PetInput", "String", "Int", "JSON"];

        Assert.That(Labels("query Q($x: |)"), Is.EqualTo(expected));
    }

    // Introspection types are part of every real schema and belong in no completion list.
    [Test]
    public void TheIntrospectionTypesAreLeftOut()
    {
        string[] expected = ["Query", "Mutation", "Subscription"];

        Assert.That(Labels("{ ... on |", roots), Is.EqualTo(expected));
    }

    [Test]
    public void ADirectivePositionOffersTheSchemasDirectives()
    {
        var entries = Complete("{ person @|");
        string[] expected = ["repeat"];

        Assert.That(Labels("{ person @|"), Is.EqualTo(expected));
        Assert.That(entries[0].Kind, Is.EqualTo("Interface"));
        Assert.That(entries[0].Detail, Is.EqualTo("directive"));
    }

    // Named spreads first, then "on Type" for the inline form — sorted after them by the z prefix.
    [Test]
    public Task AFragmentSpreadOffersTheNamedFragmentsThenTheInlineForm() =>
        Verify(Render(Complete("{ ...| }\n\nfragment Fields on Person { name }")));

    [Test]
    public void AModeWithNothingToOfferReturnsAnEmptyList() =>
        Assert.That(Complete("query Q($|)"), Is.Empty);

    // Monaco sorts by SortText, so declaration order only survives if the prefix is ascending.
    [Test]
    public void SortTextKeepsDeclarationOrder()
    {
        var sortTexts = Complete("{ | }").Select(_ => _.SortText).ToList();

        Assert.That(sortTexts, Is.EqualTo(sortTexts.Order(StringComparer.Ordinal)));
        Assert.That(sortTexts[0], Is.EqualTo("0000person"));
    }
}
