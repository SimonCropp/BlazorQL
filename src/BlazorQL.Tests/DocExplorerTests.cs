/// <summary>
/// bUnit coverage for the documentation explorer, rendered against a canned introspection result
/// (a hand-written representative subset — see DocExplorerTests.schema.json).
/// </summary>
public class DocExplorerTests
{
    // Per test: an index shared between tests run in parallel races as it fills its lookup tables.
    readonly SchemaIndex schema = LoadSchema();

    static SchemaIndex LoadSchema()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, ProjectFiles.DocExplorerTests_schema_json));
        using var document = JsonDocument.Parse(json);
        return SchemaIndex.Parse(document.RootElement)!;
    }

    IRenderedComponent<DocExplorer> Render(BunitContext context, DocExplorerNavigator? navigator = null) =>
        context.Render<DocExplorer>(_ => _
            .Add(_ => _.Schema, schema)
            .Add(_ => _.Navigator, navigator));

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
    public async Task DeprecatedFieldsToggleRevealsTheSection()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Query");

        // Hidden until asked for.
        await Assert.That(cut.Markup).DoesNotContain("oldField");

        cut.FindAll(".blazorql-doc-toggle").Single(_ => _.TextContent == "Show Deprecated Fields").Click();
        await Assert.That(cut.Markup).Contains("oldField");
        await Assert.That(cut.Markup).Contains("Deprecated Fields");
        await Assert.That(cut.FindAll(".blazorql-doc-toggle")).IsEmpty();
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
        await Assert.That(cut.Markup).DoesNotContain("GRAY");
        await cut.FindAll(".blazorql-doc-toggle").Single(_ => _.TextContent == "Show Deprecated Values").ClickAsync();
        await Assert.That(cut.Markup).Contains("GRAY");
        await Assert.That(cut.Markup).Contains("Colors are boring.");
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
    public async Task BackWalksUpTheStack()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Query");
        NavigateToField(cut, "person");

        var back = cut.Find("[data-testid='doc-back']");
        await Assert.That(back.GetAttribute("aria-label")).IsEqualTo("Go back to Query");
        back.Click();
        await Assert.That(cut.Find("[data-testid='doc-back']").GetAttribute("aria-label")).IsEqualTo("Go back to Docs");
        cut.Find("[data-testid='doc-back']").Click();
        await Assert.That(cut.FindAll("[data-testid='doc-back']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Root Types");
    }

    [Test]
    public async Task SearchMatchesTypesFieldsAndArguments()
    {
        using var context = new BunitContext();
        var cut = Render(context);

        cut.Find("[data-testid='doc-search'] input").Input("person");
        cut.WaitForState(() => cut.FindAll(".blazorql-doc-search-result").Count > 0, TimeSpan.FromSeconds(5));
        var results = cut.FindAll(".blazorql-doc-search-result").Select(_ => _.TextContent).ToList();
        await Assert.That(results).Contains("Person");
        await Assert.That(results).Contains("Query.person");

        // An argument match renders Type.field(arg: ArgType).
        cut.Find("[data-testid='doc-search'] input").Input("term");
        cut.WaitForState(
            () => cut.FindAll(".blazorql-doc-search-result").Any(_ => _.TextContent == "Query.search(term: String!)"),
            TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task SearchBucketsTheCurrentTypeFirst()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        NavigateToType(cut, "Person");

        cut.Find("[data-testid='doc-search'] input").Input("name");
        cut.WaitForState(() => cut.FindAll(".blazorql-doc-search-result").Count > 0, TimeSpan.FromSeconds(5));

        // The open type's matches come first, everything else after the divider.
        var results = cut.FindAll(".blazorql-doc-search-result").Select(_ => _.TextContent).ToList();
        await Assert.That(results[0]).IsEqualTo("Person.name");
        await Assert.That(cut.Markup).Contains("Other results");
        await Assert.That(results).Contains("Named.name");
    }

    /// <summary>
    /// 200 types of ten fields each, every field matching "field": far more matches than the cap,
    /// most of them in types the schema lists before the one that is open.
    /// </summary>
    static SchemaIndex ManyTypesSchema()
    {
        var fields = string.Join(
            ", ",
            Enumerable.Range(0, 10).Select(field =>
                $$"""{"name": "field{{field}}", "type": {"kind": "SCALAR", "name": "String"}, "isDeprecated": false, "args": []}"""));
        var types = Enumerable.Range(0, 200).Select(index =>
            $$"""{"kind": "OBJECT", "name": "Type{{index}}", "fields": [""" + fields + "]}");
        var json =
            $$$"""
              {"__schema": {
                "queryType": {"name": "Type0"},
                "types": [{{{string.Join(",\n", types)}}}, {"kind": "SCALAR", "name": "String"}],
                "directives": []
              }}
              """;
        using var document = JsonDocument.Parse(json);
        return SchemaIndex.Parse(document.RootElement)!;
    }

    /// <summary>
    /// The scan stopped at the cap in schema order, so once earlier types had filled it, the open
    /// type's own matches, the ones searching from its page is for, were never reached.
    /// </summary>
    [Test]
    public async Task SearchFindsTheOpenTypesMatchesPastTheCap()
    {
        using var context = new BunitContext();
        var navigator = new DocExplorerNavigator();
        var cut = context.Render<DocExplorer>(_ => _
            .Add(_ => _.Schema, ManyTypesSchema())
            .Add(_ => _.Navigator, navigator));
        await cut.InvokeAsync(() => navigator.NavigateTo(new("Type", "Type150")));

        cut.Find("[data-testid='doc-search'] input").Input("field");
        cut.WaitForState(() => cut.FindAll(".blazorql-doc-search-result").Count > 0, TimeSpan.FromSeconds(5));

        var results = cut.FindAll(".blazorql-doc-search-result").Select(_ => _.TextContent).ToList();
        string[] expected = [.. Enumerable.Range(0, 10).Select(_ => $"Type150.field{_}")];
        await Assert.That(results.Take(10)).IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(cut.Markup).Contains("Other results");
        // The cap still holds over the whole list.
        await Assert.That(results.Count).IsEqualTo(100);
        await Assert.That(results.Skip(10)).All(_ => !_.StartsWith("Type150.", StringComparison.Ordinal));
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
    public async Task SearchSelectionNavigatesToTheField()
    {
        await using var context = new BunitContext();
        var cut = Render(context);

        cut.Find("[data-testid='doc-search'] input").Input("hasArgs");
        cut.WaitForState(
            () => cut.FindAll(".blazorql-doc-search-result").Any(_ => _.TextContent == "Query.hasArgs"),
            TimeSpan.FromSeconds(5));
        // Awaited: the synchronous Click is fire-and-forget, and with the debounce's render still
        // draining on the dispatcher the handler could run after the assertions below.
        await cut.FindAll(".blazorql-doc-search-result").Single(_ => _.TextContent == "Query.hasArgs").ClickAsync(new());

        // The parent type page went onto the stack first, so back walks up naturally.
        await Assert.That(cut.FindAll("[data-testid='doc-field']")).IsNotEmpty();
        await Assert.That(cut.Find("[data-testid='doc-back']").GetAttribute("aria-label")).IsEqualTo("Go back to Query");
    }

    [Test]
    public async Task NavigatorJumpsToTheReferencedField()
    {
        using var context = new BunitContext();
        var navigator = new DocExplorerNavigator();
        // A reference that arrived before the explorer mounted is applied on mount.
        navigator.NavigateTo(new("Field", "Query", "person"));
        var cut = Render(context, navigator);

        await Assert.That(cut.FindAll("[data-testid='doc-field']")).IsNotEmpty();
        await Assert.That(cut.Find(".blazorql-doc-title").TextContent).IsEqualTo("person");

        // A reference while mounted navigates immediately.
        navigator.NavigateTo(new("Type", "Color"));
        cut.WaitForState(() => cut.Find(".blazorql-doc-title").TextContent == "Color", TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task GenerateButtonOnTheRootPageRaisesTheDocument()
    {
        await using var context = new BunitContext();
        string? generated = null;
        var cut = context.Render<DocExplorer>(_ => _
            .Add(_ => _.Schema, schema)
            .Add(_ => _.OnGenerateQuery, query => generated = query));

        // Only types a selection can be built over carry the button.
        var buttons = cut.FindAll("[data-testid='doc-generate']").Select(_ => _.GetAttribute("aria-label")).ToList();
        await Assert.That(buttons).Contains("Generate a query for Person");
        await Assert.That(buttons).DoesNotContain("Generate a query for Color");
        await Assert.That(buttons).DoesNotContain("Generate a query for PetInput");

        await cut.Find("[data-testid='doc-generate'][aria-label='Generate a query for Person']").ClickAsync(new());
        await Assert.That(generated).StartsWith("query Person {");
        await Assert.That(generated).Contains("  person {");
    }

    [Test]
    public async Task GenerateButtonOnATypePageRaisesTheDocument()
    {
        await using var context = new BunitContext();
        string? generated = null;
        var cut = context.Render<DocExplorer>(_ => _
            .Add(_ => _.Schema, schema)
            .Add(_ => _.OnGenerateQuery, query => generated = query));
        NavigateToType(cut, "Query");

        await cut.Find(".blazorql-docs-header [data-testid='doc-generate']").ClickAsync(new());
        await Assert.That(generated).StartsWith("query Query {");

        // An enum page has no button in the header.
        cut.Find("[data-testid='doc-back']").Click();
        NavigateToType(cut, "Color");
        await Assert.That(cut.FindAll(".blazorql-docs-header [data-testid='doc-generate']")).IsEmpty();
    }

    [Test]
    public async Task NoSchemaShowsThePlaceholder()
    {
        using var context = new BunitContext();
        var cut = context.Render<DocExplorer>();
        await Assert.That(cut.Markup).Contains("No GraphQL schema available");
    }

    [Test]
    public async Task SdlToggleIsHiddenWithoutTheSdl()
    {
        using var context = new BunitContext();
        var cut = Render(context);
        await Assert.That(cut.FindAll("[data-testid='doc-sdl']")).IsEmpty();
    }
}
