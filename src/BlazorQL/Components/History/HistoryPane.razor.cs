namespace BlazorQL;

/// <summary>
/// The history plugin's pane: favorites above the rest, a text filter over both, and per-row
/// label editing, favoriting and deletion. The store owns the data; this only drives it.
/// </summary>
public partial class HistoryPane
{
    [Parameter]
    [EditorRequired]
    public HistoryStore Store { get; set; } = null!;

    /// <summary>Raised when an item's label is clicked — the parent loads it into the editors.</summary>
    [Parameter]
    public EventCallback<HistoryItem> OnSelect { get; set; }

    string filter = "";
    HistoryItem? editing;
    string editText = "";
    bool focusEdit;
    ElementReference editInput;

    void Clear() =>
        Store.ClearNonFavorites();

    void StartEdit(HistoryItem item)
    {
        editing = item;
        editText = item.Label ?? "";
        focusEdit = true;
    }

    // Escape cancels without committing — the fix over GraphiQL, where Esc commits the edit.
    void CancelEdit() =>
        editing = null;

    void OnEditKey(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            if (editing is not null)
            {
                Store.EditLabel(editing, editText);
            }

            editing = null;
        }
        else if (args.Key == "Escape")
        {
            editing = null;
        }
    }

    void ToggleFavorite(HistoryItem item) =>
        Store.ToggleFavorite(item);

    void Delete(HistoryItem item) =>
        Store.Delete(item);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusEdit)
        {
            focusEdit = false;
            await editInput.FocusAsync();
        }
    }
}
