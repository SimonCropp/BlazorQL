namespace BlazorQL;

/// <summary>
/// The operation tab strip: activate, close, add, and an inline rename started by double-clicking
/// a tab's title. Escape cancels a rename without committing.
/// </summary>
public partial class TabBar
{
    [Parameter]
    [EditorRequired]
    public TabStore Store { get; set; } = null!;

    [Parameter]
    public EventCallback<int> OnActivate { get; set; }

    [Parameter]
    public EventCallback<int> OnClose { get; set; }

    [Parameter]
    public EventCallback OnAdd { get; set; }

    /// <summary>Raised when an inline rename commits: (tab index, new title or null to clear).</summary>
    [Parameter]
    public EventCallback<(int Index, string? Title)> OnRename { get; set; }

    /// <summary>
    /// Titles already derived, against the strings they were derived from. A title is asked for
    /// once per tab on every render of the IDE, and deriving one runs a regex over the whole
    /// document. Weakly keyed, so a closed tab's entry goes with the tab; compared by reference,
    /// because these strings are replaced rather than edited in place.
    /// </summary>
    readonly ConditionalWeakTable<TabState, DerivedTitle> titles = [];

    sealed class DerivedTitle
    {
        public string? Query { get; set; }
        public string? OperationName { get; set; }
        public string? RenameOverride { get; set; }
        public string Text { get; set; } = "";
    }

    string Title(TabState tab)
    {
        var derived = titles.GetOrCreateValue(tab);
        if (!ReferenceEquals(derived.Query, tab.Query) ||
            !ReferenceEquals(derived.OperationName, tab.OperationName) ||
            !ReferenceEquals(derived.RenameOverride, tab.RenameOverride))
        {
            derived.Query = tab.Query;
            derived.OperationName = tab.OperationName;
            derived.RenameOverride = tab.RenameOverride;
            derived.Text = TabStore.Title(tab);
        }

        return derived.Text;
    }

    int renamingIndex = -1;
    string renameText = "";
    bool focusRename;
    ElementReference renameInput;

    void StartRename(int index)
    {
        renamingIndex = index;
        renameText = Title(Store.Tabs[index]);
        focusRename = true;
    }

    void CancelRename() =>
        renamingIndex = -1;

    Task OnRenameKey(KeyboardEventArgs args, int index)
    {
        if (args.Key == "Enter")
        {
            renamingIndex = -1;
            var title = string.IsNullOrWhiteSpace(renameText)
                ? null
                : renameText.Trim();
            return OnRename.InvokeAsync((index, title));
        }

        if (args.Key == "Escape")
        {
            renamingIndex = -1;
        }

        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusRename)
        {
            focusRename = false;
            await renameInput.FocusAsync();
        }
    }
}
