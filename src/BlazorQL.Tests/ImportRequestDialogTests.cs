/// <summary>
/// bUnit coverage for the import-request dialog: parsing as the box is typed into, the summary
/// wording, and what Import hands up. Creating the tabs themselves is not testable here — nothing
/// in this suite renders <see cref="BlazorQLIde"/>, because Monaco needs a browser — so that half
/// is covered by the Playwright suite instead.
/// </summary>
[TestFixture]
public class ImportRequestDialogTests
{
    [Test]
    public void AnEmptyDialogShowsNoSummaryAndCannotImport()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Assert.That(cut.Find("[data-testid='import-dialog']").GetAttribute("role"), Is.EqualTo("dialog"));
        Assert.That(cut.Find("[data-testid='import-summary']").TextContent, Is.Empty);
        Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled"));
    }

    [Test]
    public void UnrecognisedTextKeepsImportDisabledAndShowsWhy()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, "hello world");

        var summary = cut.Find("[data-testid='import-summary']");
        Assert.That(summary.ClassList, Does.Contain("blazorql-import-invalid"));
        Assert.That(summary.TextContent, Does.StartWith("Could not recognise this."));
        Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled"));
    }

    [Test]
    public void APastedCurlEnablesImportAndSummarisesTheRequest()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, curl);

        Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled"), Is.False);
        Assert.That(
            cut.Find("[data-testid='import-summary']").TextContent,
            Is.EqualTo("mutation EnableUser · 1 variable · 1 of 3 headers imported"));
    }

    [Test]
    public void AnAnonymousOperationIsSummarisedByItsKind()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, """{"query":"{ hero { name } }"}""");

        Assert.That(cut.Find("[data-testid='import-summary']").TextContent, Is.EqualTo("query"));
    }

    /// <summary>
    /// With the headers editor off the IDE never sends tab headers, so counting them as imported
    /// would promise something that does not happen.
    /// </summary>
    [Test]
    public void HeaderCountsBecomeIgnoredWhenTheHeadersEditorIsOff()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.HeadersEnabled, false));

        Paste(cut, curl);

        var summary = cut.Find("[data-testid='import-summary']").TextContent;
        Assert.That(summary, Does.EndWith("· headers ignored"));
        Assert.That(summary, Does.Not.Contain("of 3"));
    }

    [Test]
    public void ABatchedBodySummarisesEveryOperation()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, """[{"query":"query A{a}"},{"query":"mutation B{b}"},{"query":"query C{c}"}]""");

        Assert.That(
            cut.Find("[data-testid='import-summary']").TextContent,
            Is.EqualTo("3 operations · query A, mutation B, query C"));
    }

    [Test]
    public void ImportRaisesEveryParsedRequest()
    {
        using var context = new BunitContext();
        IReadOnlyList<ImportedRequest> imported = [];
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnImport, requests => imported = requests));

        Paste(cut, curl);
        cut.Find("[data-testid='import-confirm']").Click();

        Assert.That(imported, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(imported[0].Query, Does.Contain("mutation EnableUser"));
            Assert.That(imported[0].Variables, Does.Contain("\"id\""));
            Assert.That(imported[0].Headers, Does.Contain("authorization"));
            // A single-operation document names its own tab, so the name is left unpinned.
            Assert.That(imported[0].OperationName, Is.Null);
        });
    }

    /// <summary>Clearing the box is going back to the start, not an error to be told about.</summary>
    [Test]
    public void ClearingTheTextDisablesImportWithoutAnError()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, curl);
        Paste(cut, "");

        var summary = cut.Find("[data-testid='import-summary']");
        Assert.That(summary.TextContent, Is.Empty);
        Assert.That(summary.ClassList, Does.Not.Contain("blazorql-import-invalid"));
        Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled"));
    }

    /// <summary>
    /// Enter has to stay a newline: the field is multi-line and a pasted curl is full of
    /// continuations. Ctrl-Enter is the IDE's commit chord everywhere else.
    /// </summary>
    [Test]
    public void CtrlEnterImportsAndPlainEnterDoesNot()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnImport, _ => raised++));

        Paste(cut, curl);
        cut.Find("[data-testid='import-text']").KeyDown(Key.Enter);
        Assert.That(raised, Is.Zero);

        cut.Find("[data-testid='import-text']").KeyDown(Key.Enter + Key.Control);
        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void CtrlEnterDoesNothingWhileTheTextDoesNotParse()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnImport, _ => raised++));

        Paste(cut, "hello world");
        cut.Find("[data-testid='import-text']").KeyDown(Key.Enter + Key.Control);

        Assert.That(raised, Is.Zero);
    }

    /// <summary>
    /// The shell's panel focus is turned off so the textarea can take it; Escape still has to close,
    /// which it does by bubbling to the overlay.
    /// </summary>
    [Test]
    public void EscapeOverlayClickCancelAndTheCloseButtonAllClose()
    {
        using var context = new BunitContext();
        var closed = 0;
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnClose, () => closed++));

        cut.Find(".blazorql-dialog-overlay").KeyDown("Escape");
        cut.Find(".blazorql-dialog-overlay").Click();
        cut.Find(".blazorql-dialog-close").Click();
        cut.Find("[data-testid='import-cancel']").Click();

        Assert.That(closed, Is.EqualTo(4));
    }

    static void Paste(IRenderedComponent<ImportRequestDialog> cut, string text) =>
        cut.Find("[data-testid='import-text']").Input(text);

    const string curl =
        """
        curl --url 'https://example.com/graphql' -H 'accept: application/json' -H 'content-type: application/json' -H 'authorization: Bearer abc' --data-raw '{"operationName":"EnableUser","variables":{"id":"a"},"query":"mutation EnableUser($id:ID!){enableUser(id:$id){success}}"}'
        """;
}
