using Microsoft.AspNetCore.Components.Web;

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

    int renamingIndex = -1;
    string renameText = "";
    bool focusRename;
    ElementReference renameInput;

    void StartRename(int index)
    {
        renamingIndex = index;
        renameText = TabStore.Title(Store.Tabs[index]);
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
