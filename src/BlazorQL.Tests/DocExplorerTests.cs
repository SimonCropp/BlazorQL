/// <summary>
/// bUnit coverage for the documentation explorer, rendered against a canned introspection result
/// (a hand-written representative subset — see DocExplorerTests.schema.json).
/// </summary>
[TestFixture]
public class DocExplorerTests
{
    static readonly SchemaIndex schema = LoadSchema();

    static SchemaIndex LoadSchema()
    {
        var json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "DocExplorerTests.schema.json"));
        using var document = JsonDocument.Parse(json);
        return SchemaIndex.Parse(document.RootElement)!;
    }

    static IRenderedComponent<DocExplorer> Render(BunitContext context, DocExplorerNavigator? navigator = null) =>
        context.Render<DocExplorer>(_ => _
            .Add(component => component.Schema, schema)
            .Add(component => component.Navigator, navigator));

    static void NavigateToType(IRenderedComponent<DocExplorer> cut, string name) =>
        cut.FindAll(".blazorql-type-link").First(_ => _.TextContent == name).Click();

    static void NavigateToField(IRenderedComponent<DocExplorer> cut, string name) =>
        cut.FindAll(".blazorql-field-link").First(_ => _.TextContent == name).Click();

    [Test]
    public async Task RootPage()
    {
        await using var context = new BunitContext();
        var cut = Render(context);
        await Verify(cut);
    }

    [Test]
    public async Task TypePage()
    {
        await using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Query");
        await Verify(cut);
    }

    [Test]
    public void DeprecatedFieldsToggleRevealsTheSection()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Query");

        // Hidden until asked for.
        Assert.That(cut.Markup, Does.Not.Contain("oldField"));

        cut.FindAll(".blazorql-doc-toggle").Single(_ => _.TextContent == "Show Deprecated Fields").Click();
        Assert.That(cut.Markup, Does.Contain("oldField"));
        Assert.That(cut.Markup, Does.Contain("Deprecated Fields"));
        Assert.That(cut.FindAll(".blazorql-doc-toggle"), Is.Empty);
    }

    [Test]
    public async Task FieldPage()
    {
        await using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Query");
        NavigateToField(cut, "hasArgs");
        await Verify(cut);
    }

    [Test]
    public async Task EnumTypePage()
    {
        await using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Color");
        await Verify(cut);

        // The deprecated value sits behind its own toggle.
        Assert.That(cut.Markup, Does.Not.Contain("GRAY"));
        await cut.FindAll(".blazorql-doc-toggle").Single(_ => _.TextContent == "Show Deprecated Values").ClickAsync();
        Assert.That(cut.Markup, Does.Contain("GRAY"));
        Assert.That(cut.Markup, Does.Contain("Colors are boring."));
    }

    [Test]
    public async Task UnionTypePage()
    {
        await using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "SearchResult");
        await Verify(cut);
    }

    [Test]
    public async Task InputObjectTypePage()
    {
        await using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "PetInput");
        await Verify(cut);
    }

    [Test]
    public void BackWalksUpTheStack()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Query");
        NavigateToField(cut, "person");

        var back = cut.Find("[data-testid='doc-back']");
        Assert.That(back.GetAttribute("aria-label"), Is.EqualTo("Go back to Query"));
        back.Click();
        Assert.That(cut.Find("[data-testid='doc-back']").GetAttribute("aria-label"), Is.EqualTo("Go back to Docs"));
        cut.Find("[data-testid='doc-back']").Click();
        Assert.That(cut.FindAll("[data-testid='doc-back']"), Is.Empty);
        Assert.That(cut.Markup, Does.Contain("Root Types"));
    }

    [Test]
    public void SearchMatchesTypesFieldsAndArguments()
    {
        using var context = new BunitContext();
        var cut = Render(context);

        cut.Find("[data-testid='doc-search'] input").Input("person");
        cut.WaitForState(() => cut.FindAll(".blazorql-doc-search-result").Count > 0, TimeSpan.FromSeconds(5));
        var results = cut.FindAll(".blazorql-doc-search-result").Select(_ => _.TextContent).ToList();
        Assert.That(results, Does.Contain("Person"));
        Assert.That(results, Does.Contain("Query.person"));

        // An argument match renders Type.field(arg: ArgType).
        cut.Find("[data-testid='doc-search'] input").Input("term");
        cut.WaitForState(
            () => cut.FindAll(".blazorql-doc-search-result").Any(_ => _.TextContent == "Query.search(term: String!)"),
            TimeSpan.FromSeconds(5));
    }

    [Test]
    public void SearchBucketsTheCurrentTypeFirst()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Person");

        cut.Find("[data-testid='doc-search'] input").Input("name");
        cut.WaitForState(() => cut.FindAll(".blazorql-doc-search-result").Count > 0, TimeSpan.FromSeconds(5));

        // The open type's matches come first, everything else after the divider.
        var results = cut.FindAll(".blazorql-doc-search-result").Select(_ => _.TextContent).ToList();
        Assert.That(results[0], Is.EqualTo("Person.name"));
        Assert.That(cut.Markup, Does.Contain("Other results"));
        Assert.That(results, Does.Contain("Named.name"));
    }

    [Test]
    public void SearchShowsTheEmptyState()
    {
        using var context = new BunitContext();
        var cut = Render(context);

        cut.Find("[data-testid='doc-search'] input").Input("zzzz");
        cut.WaitForState(() => cut.Markup.Contains("No results found"), TimeSpan.FromSeconds(5));
    }

    [Test]
    public void SearchSelectionNavigatesToTheField()
    {
        using var context = new BunitContext();
        var cut = Render(context);

        cut.Find("[data-testid='doc-search'] input").Input("hasArgs");
        cut.WaitForState(
            () => cut.FindAll(".blazorql-doc-search-result").Any(_ => _.TextContent == "Query.hasArgs"),
            TimeSpan.FromSeconds(5));
        cut.FindAll(".blazorql-doc-search-result").Single(_ => _.TextContent == "Query.hasArgs").Click();

        // The parent type page went onto the stack first, so back walks up naturally.
        Assert.That(cut.FindAll("[data-testid='doc-field']"), Is.Not.Empty);
        Assert.That(cut.Find("[data-testid='doc-back']").GetAttribute("aria-label"), Is.EqualTo("Go back to Query"));
    }

    [Test]
    public void NavigatorJumpsToTheReferencedField()
    {
        using var context = new BunitContext();
        var navigator = new DocExplorerNavigator();
        // A reference that arrived before the explorer mounted is applied on mount.
        navigator.NavigateTo(new("Field", "Query", "person"));
        var cut = Render(context, navigator);

        Assert.That(cut.FindAll("[data-testid='doc-field']"), Is.Not.Empty);
        Assert.That(cut.Find(".blazorql-doc-title").TextContent, Is.EqualTo("person"));

        // A reference while mounted navigates immediately.
        navigator.NavigateTo(new("Type", "Color"));
        cut.WaitForState(() => cut.Find(".blazorql-doc-title").TextContent == "Color", TimeSpan.FromSeconds(5));
    }

    [Test]
    public void NoSchemaShowsThePlaceholder()
    {
        using var context = new BunitContext();
        var cut = context.Render<DocExplorer>();
        Assert.That(cut.Markup, Does.Contain("No GraphQL schema available"));
    }

    [Test]
    public void SdlToggleIsHiddenWithoutTheSdl()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        Assert.That(cut.FindAll("[data-testid='doc-sdl']"), Is.Empty);
    }
}
