/// <summary>bUnit coverage for the tab strip's add, duplicate, and import buttons.</summary>
public class TabBarTests
{
    [Test]
    public async Task TheImportButtonRaisesOnImport()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store())
            .Add(component => component.OnImport, () => raised++));

        cut.Find("[data-testid='tab-import']").Click();

        await Assert.That(raised).IsEqualTo(1);
    }

    /// <summary>
    /// Both labels carry the same text, as every other icon button in the IDE does — and it is the
    /// wording docs/features.md describes, so a drift here is a drift from the docs.
    /// </summary>
    [Test]
    public async Task TheImportButtonLabelsItselfForPointerAndScreenReaderAlike()
    {
        using var context = new BunitContext();
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store()));
        var button = cut.Find("[data-testid='tab-import']");

        await Assert.That(button.GetAttribute("title")).IsEqualTo("Import request into a new tab");
        await Assert.That(button.GetAttribute("aria-label")).IsEqualTo("Import request into a new tab");
    }

    [Test]
    public async Task TheAddButtonStillRaisesOnAdd()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store())
            .Add(component => component.OnAdd, () => raised++));

        cut.Find("[data-testid='tab-add']").Click();

        await Assert.That(raised).IsEqualTo(1);
    }

    [Test]
    public async Task TheDuplicateButtonRaisesOnDuplicate()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store())
            .Add(component => component.OnDuplicate, () => raised++));

        cut.Find("[data-testid='tab-duplicate']").Click();

        await Assert.That(raised).IsEqualTo(1);
    }

    /// <summary>Both labels carry the same text, as they do on the strip's other buttons.</summary>
    [Test]
    public async Task TheDuplicateButtonLabelsItselfForPointerAndScreenReaderAlike()
    {
        using var context = new BunitContext();
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store()));
        var button = cut.Find("[data-testid='tab-duplicate']");

        await Assert.That(button.GetAttribute("title")).IsEqualTo("Duplicate tab");
        await Assert.That(button.GetAttribute("aria-label")).IsEqualTo("Duplicate tab");
    }

    static TabStore Store()
    {
        var store = new TabStore();
        store.Add("query A { id }");
        return store;
    }
}