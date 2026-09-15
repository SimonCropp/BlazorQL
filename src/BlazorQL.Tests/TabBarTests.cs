/// <summary>bUnit coverage for the tab strip's add, duplicate, and import buttons.</summary>
[TestFixture]
public class TabBarTests
{
    [Test]
    public void TheImportButtonRaisesOnImport()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store())
            .Add(component => component.OnImport, () => raised++));

        cut.Find("[data-testid='tab-import']").Click();

        Assert.That(raised, Is.EqualTo(1));
    }

    /// <summary>
    /// Both labels carry the same text, as every other icon button in the IDE does — and it is the
    /// wording docs/features.md describes, so a drift here is a drift from the docs.
    /// </summary>
    [Test]
    public void TheImportButtonLabelsItselfForPointerAndScreenReaderAlike()
    {
        using var context = new BunitContext();
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store()));
        var button = cut.Find("[data-testid='tab-import']");

        Assert.That(button.GetAttribute("title"), Is.EqualTo("Import request into a new tab"));
        Assert.That(button.GetAttribute("aria-label"), Is.EqualTo("Import request into a new tab"));
    }

    [Test]
    public void TheAddButtonStillRaisesOnAdd()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store())
            .Add(component => component.OnAdd, () => raised++));

        cut.Find("[data-testid='tab-add']").Click();

        Assert.That(raised, Is.EqualTo(1));
    }

    [Test]
    public void TheDuplicateButtonRaisesOnDuplicate()
    {
        using var context = new BunitContext();
        var raised = 0;
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store())
            .Add(component => component.OnDuplicate, () => raised++));

        cut.Find("[data-testid='tab-duplicate']").Click();

        Assert.That(raised, Is.EqualTo(1));
    }

    /// <summary>Both labels carry the same text, as they do on the strip's other buttons.</summary>
    [Test]
    public void TheDuplicateButtonLabelsItselfForPointerAndScreenReaderAlike()
    {
        using var context = new BunitContext();
        var cut = context.Render<TabBar>(_ => _
            .Add(component => component.Store, Store()));
        var button = cut.Find("[data-testid='tab-duplicate']");

        Assert.That(button.GetAttribute("title"), Is.EqualTo("Duplicate tab"));
        Assert.That(button.GetAttribute("aria-label"), Is.EqualTo("Duplicate tab"));
    }

    static TabStore Store()
    {
        var store = new TabStore();
        store.Add("query A { id }");
        return store;
    }
}
