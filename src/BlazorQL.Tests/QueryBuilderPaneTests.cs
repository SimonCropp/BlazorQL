using AngleSharp.Dom;

/// <summary>
/// bUnit coverage for the query builder pane over <see cref="BuilderSchema"/>. A small host plays
/// the IDE — each edit the pane raises is applied to the text and the pane redrawn from the result —
/// so every test reads as clicks and the document they leave behind.
/// </summary>
public class QueryBuilderPaneTests
{
    sealed class Host
    {
        public string Text { get; set; } = "";

        public int Edits { get; private set; }

        public IRenderedComponent<QueryBuilderPane> Pane { get; set; } = null!;

        public void Apply(Func<string, string?> edit)
        {
            if (edit(Text) is not { } edited)
            {
                return;
            }

            Text = edited;
            Edits++;
        }

        /// <summary>What the IDE does once an edit lands: the tab records the text, and the pane redraws from it.</summary>
        public void Refresh() =>
            Pane.Render(_ => _.Add(component => component.Query, Text));

        public IElement Find(string selector) =>
            Pane.Find(selector);

        public void Click(string selector)
        {
            Find(selector).Click();
            Refresh();
        }
    }

    static Host Render(BunitContext context, string text)
    {
        var host = new Host
        {
            Text = text
        };
        host.Pane = context.Render<QueryBuilderPane>(_ => _
            .Add(component => component.Schema, BuilderSchema.Create())
            .Add(component => component.Query, text)
            .Add(component => component.OnEdit, host.Apply));
        return host;
    }

    static string Field(string name) =>
        $"[data-testid='builder-field'][data-name='{name}']";

    static string Argument(string name) =>
        $"[data-testid='builder-argument'][data-name='{name}']";

    static string Checked(Host host, string selector) =>
        host.Find(selector).GetAttribute("aria-checked")!;

    // A blank tab, or one holding only the welcome comments, has no operation to draw — so the pane
    // offers a query to start, rather than an empty tree.
    [Test]
    public async Task OffersAQueryToStartFromABlankDocument()
    {
        await using var context = new BunitContext();
        var host = Render(context, "# Welcome\n");

        var pending = host.Find("[data-testid='builder-pending']");
        await Assert.That(pending.TextContent).Contains("MyQuery");
        string[] expected = ["user", "users", "search", "node", "viewer", "version"];
        await Assert.That(pending.QuerySelectorAll("[data-testid='builder-field']").Select(_ => _.GetAttribute("data-name")!))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task StartsTheQueryWithTheFirstFieldChosen()
    {
        await using var context = new BunitContext();
        var host = Render(context, "# Welcome\n");

        host.Click($"[data-testid='builder-pending'] {Field("version")}");

        await Assert.That(host.Text).IsEqualTo("# Welcome\n\nquery MyQuery {\n  version\n}\n");
        await Assert.That(host.Pane.FindAll("[data-testid='builder-pending']")).IsEmpty();
        await Assert.That(host.Find("[data-testid='builder-operation-name']").GetAttribute("value")).IsEqualTo("MyQuery");
        await Assert.That(Checked(host, Field("version"))).IsEqualTo("true");
    }

    // The boxes are the text: what it selects is checked, and a field it selects opens on its members.
    [Test]
    public async Task ChecksWhatTheTextSelects()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ viewer { id name } }");

        await Assert.That(Checked(host, Field("viewer"))).IsEqualTo("true");
        await Assert.That(Checked(host, Field("name"))).IsEqualTo("true");
        await Assert.That(Checked(host, Field("role"))).IsEqualTo("false");
        await Assert.That(Checked(host, Field("version"))).IsEqualTo("false");
        await Assert.That(host.Find($"{Field("viewer")} .blazorql-builder-caret").ClassList).Contains("blazorql-expanded");
    }

    [Test]
    public async Task TogglesAFieldOnClick()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ version }");

        host.Click(Field("viewer"));
        await Assert.That(host.Text).IsEqualTo("{ viewer { id } version }");
        await Assert.That(Checked(host, Field("id"))).IsEqualTo("true");

        host.Click(Field("viewer"));
        await Assert.That(host.Text).IsEqualTo("{ version }");
    }

    // A document that does not parse cannot be drawn, and editing a guess at it could lose work.
    [Test]
    public async Task ExplainsTextThatDoesNotParse()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ version");

        await Assert.That(host.Find("[data-testid='builder-notice']").TextContent).Contains("does not parse");
        await Assert.That(host.Pane.FindAll("[data-testid='builder']")).IsEmpty();
    }

    [Test]
    public async Task ShowsTheArgumentsOfASelectedField()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ users { id } }");

        await Assert.That(Checked(host, Argument("role"))).IsEqualTo("false");

        host.Click(Argument("role"));
        await Assert.That(host.Text).IsEqualTo("{ users(role: ADMIN) { id } }");

        var select = host.Find("select[data-testid='builder-value']");
        await Assert.That(select.QuerySelector("option[selected]")!.TextContent).IsEqualTo("ADMIN");
    }

    [Test]
    public async Task ChoosesAnEnumValue()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ users(role: ADMIN) { id } }");

        host.Find("select[data-testid='builder-value']").Change("EDITOR");

        await Assert.That(host.Text).IsEqualTo("{ users(role: EDITOR) { id } }");
    }

    [Test]
    public async Task WritesAValueTypedIn()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ users(first: 1) { id } }");

        var input = host.Find("input[data-testid='builder-value']");
        await Assert.That(input.GetAttribute("value")).IsEqualTo("1");

        input.Change("25");
        await Assert.That(host.Text).IsEqualTo("{ users(first: 25) { id } }");
    }

    // Letters in a number would be a literal the server refuses; the text is left alone and the input
    // says so.
    [Test]
    public async Task RefusesAValueThatIsNotOfItsType()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ users(first: 1) { id } }");

        host.Find("input[data-testid='builder-value']").Change("ten");

        await Assert.That(host.Edits).IsEqualTo(0);
        var input = host.Find("input[data-testid='builder-value']");
        await Assert.That(input.ClassList).Contains("blazorql-invalid");
        await Assert.That(input.GetAttribute("aria-invalid")).IsEqualTo("true");
    }

    [Test]
    public async Task OpensAnInputObjectOnItsFields()
    {
        await using var context = new BunitContext();
        var host = Render(context, """{ users(filter: {required: ""}) { id } }""");

        await Assert.That(Checked(host, Argument("required"))).IsEqualTo("true");
        await Assert.That(Checked(host, Argument("name"))).IsEqualTo("false");

        host.Click(Argument("name"));
        await Assert.That(host.Text).IsEqualTo("""{ users(filter: {name: "", required: ""}) { id } }""");
    }

    [Test]
    public async Task TurnsAnArgumentIntoAVariable()
    {
        await using var context = new BunitContext();
        var host = Render(context, """query Q { user(id: "7") { id } }""");

        host.Click("[data-testid='builder-variable']");

        await Assert.That(host.Text).IsEqualTo("""query Q($id: ID! = "7") { user(id: $id) { id } }""");
        await Assert.That(host.Find("[data-testid='builder-value']").TextContent).IsEqualTo("$id");
        await Assert.That(host.Find("[data-testid='builder-variable']").GetAttribute("aria-pressed")).IsEqualTo("true");
    }

    // A fragment cannot declare a variable, so its arguments offer none.
    [Test]
    public async Task OffersNoVariablesInsideAFragment()
    {
        await using var context = new BunitContext();
        var host = Render(context, """fragment F on Query { user(id: "7") { id } }""");

        await Assert.That(host.Find("[data-testid='builder-fragment-definition']").TextContent).Contains("Query");
        await Assert.That(host.Pane.FindAll("[data-testid='builder-variable']")).IsEmpty();
    }

    [Test]
    public async Task RenamesAnOperation()
    {
        await using var context = new BunitContext();
        var host = Render(context, "query Q { version }");

        host.Find("[data-testid='builder-operation-name']").Change("Renamed");

        await Assert.That(host.Text).IsEqualTo("query Renamed { version }");
    }

    [Test]
    public async Task RemovesAnOperation()
    {
        await using var context = new BunitContext();
        var host = Render(context, "query A { version }\n\nquery B { viewer { id } }\n");

        host.Click("[data-testid='builder-remove-operation']");

        await Assert.That(host.Text).IsEqualTo("query B { viewer { id } }\n");
    }

    // A deprecated field stays out of the tree, unless the text selects it: then it is there to uncheck.
    [Test]
    public async Task ShowsADeprecatedFieldOnlyWhenSelected()
    {
        await using var context = new BunitContext();

        await Assert.That(Render(context, "{ version }").Pane.FindAll(Field("legacy"))).IsEmpty();
        await Assert.That(Checked(Render(context, "{ legacy }"), Field("legacy"))).IsEqualTo("true");
    }

    [Test]
    public async Task SelectsAUnionMemberThroughAnInlineFragment()
    {
        await using var context = new BunitContext();
        var host = Render(context, """{ search(term: "x") { __typename } }""");

        string[] members = ["User", "Post"];
        await Assert.That(host.Pane.FindAll("[data-testid='builder-inline-fragment']").Select(_ => _.GetAttribute("data-name")!))
            .IsEquivalentTo(members, CollectionOrdering.Matching);

        host.Click("[data-testid='builder-inline-fragment'][data-name='Post']");
        await Assert.That(host.Text).IsEqualTo("""{ search(term: "x") { __typename ... on Post { id } } }""");
    }

    [Test]
    public async Task SpreadsAFragmentOnTheType()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ viewer { id } }\n\nfragment UserFields on User { name }\n");

        host.Click("[data-testid='builder-operation'] [data-testid='builder-spread'][data-name='UserFields']");

        await Assert.That(host.Text).IsEqualTo("{ viewer { id ...UserFields } }\n\nfragment UserFields on User { name }\n");
    }

    // The add buttons are the kinds the schema has roots for; this one has no subscriptions.
    [Test]
    public async Task OffersTheOperationKindsTheSchemaHas()
    {
        await using var context = new BunitContext();
        var host = Render(context, "{ version }");

        string[] expected = ["query", "mutation"];
        await Assert.That(host.Pane.FindAll("[data-testid='builder-add']").Select(_ => _.GetAttribute("data-kind")!))
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AddsAMutationBesideAQuery()
    {
        await using var context = new BunitContext();
        var host = Render(context, "query Q { version }\n");

        host.Click("[data-testid='builder-add'][data-kind='mutation']");
        host.Click($"[data-testid='builder-pending'] {Field("rename")}");

        await Assert.That(host.Text).IsEqualTo(
            """
            query Q { version }

            mutation MyMutation {
              rename(id: "", name: "") {
                id
              }
            }

            """);
    }

    [Test]
    public async Task Snapshot()
    {
        await using var context = new BunitContext();
        var host = Render(
            context,
            """
            query Users($role: Role) {
              users(first: 10, role: $role) {
                id
                name
              }
            }
            """);

        // The schema is the fixture's, and its every directive and type would bury the pane's own
        // parameters.
        await Verify(host.Pane)
            .IgnoreMember<QueryBuilderPane>(_ => _.Schema);
    }
}
