/// <summary>
/// bUnit coverage for the import-request dialog: parsing as the box is typed into, the summary
/// wording, and what Import hands up. Creating the tabs themselves is not testable here — nothing
/// in this suite renders <see cref="BlazorQLIde"/>, because Monaco needs a browser — so that half
/// is covered by the Playwright suite instead.
/// </summary>
public class ImportRequestDialogTests
{
    [Test]
    public async Task AnEmptyDialogShowsNoSummaryAndCannotImport()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        await Assert.That(cut.Find("[data-testid='import-dialog']").GetAttribute("role")).IsEqualTo("dialog");
        await Assert.That(cut.Find("[data-testid='import-summary']").TextContent).IsEmpty();
        await Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task UnrecognisedTextKeepsImportDisabledAndShowsWhy()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, "hello world");

        var summary = cut.Find("[data-testid='import-summary']");
        await Assert.That(summary.ClassList).Contains("blazorql-import-invalid");
        await Assert.That(summary.TextContent).StartsWith("Could not recognise this.");
        await Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task APastedCurlEnablesImportAndSummarisesTheRequest()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, curl);

        await Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.Find("[data-testid='import-summary']").TextContent).IsEqualTo("mutation EnableUser · 1 variable · 1 of 3 headers imported");
    }

    [Test]
    public async Task AnAnonymousOperationIsSummarisedByItsKind()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, """{"query":"{ hero { name } }"}""");

        await Assert.That(cut.Find("[data-testid='import-summary']").TextContent).IsEqualTo("query");
    }

    /// <summary>
    /// With the headers editor off the IDE never sends tab headers, so counting them as imported
    /// would promise something that does not happen.
    /// </summary>
    [Test]
    public async Task HeaderCountsBecomeIgnoredWhenTheHeadersEditorIsOff()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.HeadersEnabled, false));

        Paste(cut, curl);

        var summary = cut.Find("[data-testid='import-summary']").TextContent;
        await Assert.That(summary).EndsWith("· headers ignored");
        await Assert.That(summary).DoesNotContain("of 3");
    }

    [Test]
    public async Task ABatchedBodySummarisesEveryOperation()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, """[{"query":"query A{a}"},{"query":"mutation B{b}"},{"query":"query C{c}"}]""");

        await Assert.That(cut.Find("[data-testid='import-summary']").TextContent).IsEqualTo("3 operations · query A, mutation B, query C");
    }

    [Test]
    public async Task ImportRaisesEveryParsedRequest()
    {
        using var context = new BunitContext();
        IReadOnlyList<ImportedRequest> imported = [];
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnImport, requests => imported = requests));

        Paste(cut, curl);
        cut.Find("[data-testid='import-confirm']").Click();

        await Assert.That(imported).Count().IsEqualTo(1);
        using (Assert.Multiple())
        {
            await Assert.That(imported[0].Query).Contains("mutation EnableUser");
            await Assert.That(imported[0].Variables).Contains("\"id\"");
            await Assert.That(imported[0].Headers).Contains("authorization");
            // A single-operation document names its own tab, so the name is left unpinned.
            await Assert.That(imported[0].OperationName).IsNull();
        }
    }

    /// <summary>Clearing the box is going back to the start, not an error to be told about.</summary>
    [Test]
    public async Task ClearingTheTextDisablesImportWithoutAnError()
    {
        using var context = new BunitContext();
        var cut = context.Render<ImportRequestDialog>();

        Paste(cut, curl);
        Paste(cut, "");

        var summary = cut.Find("[data-testid='import-summary']");
        await Assert.That(summary.TextContent).IsEmpty();
        await Assert.That(summary.ClassList).DoesNotContain("blazorql-import-invalid");
        await Assert.That(cut.Find("[data-testid='import-confirm']").HasAttribute("disabled")).IsTrue();
    }

    /// <summary>
    /// Enter has to stay a newline: the field is multi-line and a pasted curl is full of
    /// continuations. Ctrl-Enter is the IDE's commit chord everywhere else.
    /// </summary>
    [Test]
    public async Task CtrlEnterImportsAndPlainEnterDoesNot()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnImport, _ => raised++));

        Paste(cut, curl);
        cut.Find("[data-testid='import-text']").KeyDown(Key.Enter);
        await Assert.That(raised).IsZero();

        cut.Find("[data-testid='import-text']").KeyDown(Key.Enter + Key.Control);
        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task CtrlEnterDoesNothingWhileTheTextDoesNotParse()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnImport, _ => raised++));

        Paste(cut, "hello world");
        cut.Find("[data-testid='import-text']").KeyDown(Key.Enter + Key.Control);

        await Assert.That(raised).IsZero();
    }

    /// <summary>
    /// The shell's panel focus is turned off so the textarea can take it; Escape still has to close,
    /// which it does by bubbling to the overlay.
    /// </summary>
    [Test]
    public async Task EscapeOverlayClickCancelAndTheCloseButtonAllClose()
    {
        using var context = new BunitContext();
        var closed = 0;
        var cut = context.Render<ImportRequestDialog>(_ => _
            .Add(component => component.OnClose, () => closed++));

        cut.Find(".blazorql-dialog-overlay").KeyDown("Escape");
        cut.Find(".blazorql-dialog-overlay").Click();
        cut.Find(".blazorql-dialog-close").Click();
        cut.Find("[data-testid='import-cancel']").Click();

        await Assert.That(closed).IsEqualTo(4);
    }

    static void Paste(IRenderedComponent<ImportRequestDialog> cut, string text) =>
        cut.Find("[data-testid='import-text']").Input(text);

    const string curl =
        """
        curl --url 'https://example.com/graphql' -H 'accept: application/json' -H 'content-type: application/json' -H 'authorization: Bearer abc' --data-raw '{"operationName":"EnableUser","variables":{"id":"a"},"query":"mutation EnableUser($id:ID!){enableUser(id:$id){success}}"}'
        """;
}