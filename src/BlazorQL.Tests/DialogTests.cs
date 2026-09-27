/// <summary>bUnit coverage for the settings and short-keys dialogs.</summary>
public class DialogTests
{
    [Test]
    public async Task SettingsRendersAllThreeSections()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>();

        await Assert.That(cut.Find("[data-testid='settings-dialog']").GetAttribute("role")).IsEqualTo("dialog");
        await Assert.That(cut.Markup).Contains("Persist headers");
        await Assert.That(cut.Markup).Contains("Save headers upon reloading.");
        await Assert.That(cut.Markup).Contains("Only enable if you trust this device.");
        await Assert.That(cut.Markup).Contains("Theme");
        await Assert.That(cut.Markup).Contains("Clear storage");
    }

    [Test]
    public async Task PersistHeadersSectionHiddenWithoutHeadersEditor()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ShowPersistHeaders, false));

        await Assert.That(cut.Markup).DoesNotContain("Persist headers");
        await Assert.That(cut.Markup).DoesNotContain("Only enable if you trust this device.");
    }

    [Test]
    public async Task ThemeSectionHiddenWhenForced()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ShowTheme, false));

        await Assert.That(cut.FindAll("[data-testid='theme-system']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='theme-light']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='theme-dark']")).IsEmpty();
    }

    [Test]
    public async Task ThemeButtonsReportTheCurrentChoiceAndRaiseSelection()
    {
        using var context = new BunitContext();
        Theme? selected = null;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.Theme, Theme.Dark)
            .Add(component => component.OnThemeSelected, theme => selected = theme));

        await Assert.That(cut.Find("[data-testid='theme-dark']").ClassList).Contains("blazorql-active");

        cut.Find("[data-testid='theme-light']").Click();
        await Assert.That(selected).IsEqualTo(Theme.Light);
    }

    [Test]
    public async Task PersistHeadersButtonsRaiseTheChoice()
    {
        using var context = new BunitContext();
        bool? persisted = null;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.OnPersistHeadersChanged, value => persisted = value));

        cut.Find("[data-testid='persist-headers-on']").Click();
        await Assert.That(persisted).IsTrue();

        cut.Find("[data-testid='persist-headers-off']").Click();
        await Assert.That(persisted).IsFalse();
    }

    [Test]
    public async Task ClearDataFlipsToClearedAndDisables()
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
        await Assert.That(button.TextContent).IsEqualTo("Clear data");

        button.Click();
        await Assert.That(cleared).IsTrue();
        var after = cut.Find("[data-testid='clear-storage']");
        await Assert.That(after.TextContent).IsEqualTo("Cleared data");
        await Assert.That(after.HasAttribute("disabled")).IsTrue();
    }

    [Test]
    public async Task ClearDataReportsFailure()
    {
        using var context = new BunitContext();
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.ClearStorageAction, () => false));

        cut.Find("[data-testid='clear-storage']").Click();
        await Assert.That(cut.Find("[data-testid='clear-storage']").TextContent).IsEqualTo("Failed");
    }

    [Test]
    public async Task EscapeAndOverlayClickClose()
    {
        using var context = new BunitContext();
        var closed = 0;
        var cut = context.Render<SettingsDialog>(_ => _
            .Add(component => component.OnClose, () => closed++));

        cut.Find(".blazorql-dialog-overlay").KeyDown("Escape");
        await Assert.That(closed).IsEqualTo(1);

        cut.Find(".blazorql-dialog-overlay").Click();
        await Assert.That(closed).IsEqualTo(2);

        cut.Find(".blazorql-dialog-close").Click();
        await Assert.That(closed).IsEqualTo(3);
    }

    [Test]
    public async Task ShortKeysListsEveryDocumentedShortcut()
    {
        using var context = new BunitContext();
        var cut = context.Render<ShortKeysDialog>();

        await Assert.That(cut.Find("[data-testid='shortkeys-dialog']").GetAttribute("role")).IsEqualTo("dialog");
        // Header row plus the nine shortcuts.
        await Assert.That(cut.FindAll(".blazorql-shortkeys-table tbody tr")).Count().IsEqualTo(9);
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
            await Assert.That(cut.Markup).Contains(expected);
        }

        await Assert.That(cut.Markup).Contains("Ctrl-Enter");
        await Assert.That(cut.Markup).Contains("Monaco/VS Code keybindings");
    }
}