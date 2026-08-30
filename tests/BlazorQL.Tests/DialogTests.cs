/// <summary>bUnit coverage for the settings and short-keys dialogs.</summary>
[TestFixture]
public class DialogTests
{
    [Test]
    public void SettingsRendersAllThreeSections()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>();

        Assert.That(cut.Find("[data-testid='settings-dialog']").GetAttribute("role"), Is.EqualTo("dialog"));
        Assert.That(cut.Markup, Does.Contain("Persist headers"));
        Assert.That(cut.Markup, Does.Contain("Save headers upon reloading."));
        Assert.That(cut.Markup, Does.Contain("Only enable if you trust this device."));
        Assert.That(cut.Markup, Does.Contain("Theme"));
        Assert.That(cut.Markup, Does.Contain("Clear storage"));
    }

    [Test]
    public void PersistHeadersSectionHiddenWithoutHeadersEditor()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ShowPersistHeaders, false));

        Assert.That(cut.Markup, Does.Not.Contain("Persist headers"));
        Assert.That(cut.Markup, Does.Not.Contain("Only enable if you trust this device."));
    }

    [Test]
    public void ThemeSectionHiddenWhenForced()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ShowTheme, false));

        Assert.That(cut.FindAll("[data-testid='theme-system']"), Is.Empty);
        Assert.That(cut.FindAll("[data-testid='theme-light']"), Is.Empty);
        Assert.That(cut.FindAll("[data-testid='theme-dark']"), Is.Empty);
    }

    [Test]
    public void ThemeButtonsReportTheCurrentChoiceAndRaiseSelection()
    {
        using var context = new BunitContext();
        Theme? selected = null;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.Theme, Theme.Dark)
            .Add(component => component.OnThemeSelected, theme => selected = theme));

        Assert.That(cut.Find("[data-testid='theme-dark']").ClassList, Does.Contain("blazorql-active"));

        cut.Find("[data-testid='theme-light']").Click();
        Assert.That(selected, Is.EqualTo(Theme.Light));
    }

    [Test]
    public void PersistHeadersButtonsRaiseTheChoice()
    {
        using var context = new BunitContext();
        bool? persisted = null;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.OnPersistHeadersChanged, value => persisted = value));

        cut.Find("[data-testid='persist-headers-on']").Click();
        Assert.That(persisted, Is.True);

        cut.Find("[data-testid='persist-headers-off']").Click();
        Assert.That(persisted, Is.False);
    }

    [Test]
    public void ClearDataFlipsToClearedAndDisables()
    {
        using var context = new BunitContext();
        var cleared = false;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ClearStorageAction, () =>
            {
                cleared = true;
                return true;
            }));

        var button = cut.Find("[data-testid='clear-storage']");
        Assert.That(button.TextContent, Is.EqualTo("Clear data"));

        button.Click();
        Assert.That(cleared, Is.True);
        var after = cut.Find("[data-testid='clear-storage']");
        Assert.That(after.TextContent, Is.EqualTo("Cleared data"));
        Assert.That(after.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void ClearDataReportsFailure()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ClearStorageAction, () => false));

        cut.Find("[data-testid='clear-storage']").Click();
        Assert.That(cut.Find("[data-testid='clear-storage']").TextContent, Is.EqualTo("Failed"));
    }

    [Test]
    public void EscapeAndOverlayClickClose()
    {
        using var context = new BunitContext();
        var closed = 0;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.OnClose, () => closed++));

        cut.Find(".blazorql-dialog-overlay").KeyDown("Escape");
        Assert.That(closed, Is.EqualTo(1));

        cut.Find(".blazorql-dialog-overlay").Click();
        Assert.That(closed, Is.EqualTo(2));

        cut.Find(".blazorql-dialog-close").Click();
        Assert.That(closed, Is.EqualTo(3));
    }

    [Test]
    public void ShortKeysListsEveryDocumentedShortcut()
    {
        using var context = new BunitContext();
        var cut = context.Render<ShortKeysDialog>();

        Assert.That(cut.Find("[data-testid='shortkeys-dialog']").GetAttribute("role"), Is.EqualTo("dialog"));
        // Header row plus the nine shortcuts.
        Assert.That(cut.FindAll(".blazorql-shortkeys-table tbody tr"), Has.Count.EqualTo(9));
        foreach (var expected in (string[])
                 [
                     "Execute query",
                     "Prettify editors",
                     "Copy query",
                     "Merge fragments",
                     "Re-fetch schema",
                     "Open settings dialog",
                     "Search in editor",
                     "Open command palette",
                     "Search in documentation"
                 ])
        {
            Assert.That(cut.Markup, Does.Contain(expected));
        }

        Assert.That(cut.Markup, Does.Contain("Ctrl-Enter"));
        Assert.That(cut.Markup, Does.Contain("Monaco/VS Code keybindings"));
    }
}
