namespace BlazorQL;

/// <summary>
/// The settings dialog: header persistence, theme, and clearing everything the IDE has stored.
/// Each section is hidden when the IDE's configuration leaves nothing to choose.
/// </summary>
public partial class SettingsDialog
{
    enum ClearState
    {
        Idle,
        Cleared,
        Failed
    }

    /// <summary>False hides the whole persist-headers section — no headers editor, nothing to persist.</summary>
    [Parameter]
    public bool ShowPersistHeaders { get; set; } = true;

    [Parameter]
    public bool PersistHeaders { get; set; }

    [Parameter]
    public EventCallback<bool> OnPersistHeadersChanged { get; set; }

    /// <summary>False hides the theme section — a forced theme leaves nothing to choose.</summary>
    [Parameter]
    public bool ShowTheme { get; set; } = true;

    [Parameter]
    public Theme Theme { get; set; }

    [Parameter]
    public EventCallback<Theme> OnThemeSelected { get; set; }

    /// <summary>Clears storage; false = the clear failed.</summary>
    [Parameter]
    public Func<bool>? ClearStorageAction { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    ClearState clearState;

    string ClearLabel =>
        clearState switch
        {
            ClearState.Cleared => "Cleared data",
            ClearState.Failed => "Failed",
            _ => "Clear data"
        };

    void ClearStorage()
    {
        var ok = ClearStorageAction?.Invoke() ?? false;
        clearState = ok
            ? ClearState.Cleared
            : ClearState.Failed;

        // The confirmation label reverts on its own after a moment, as in GraphiQL. Fired and
        // forgotten so the click handler itself completes immediately.
        _ = RevertAfterDelay();
    }

    async Task RevertAfterDelay()
    {
        await Task.Delay(2000);
        clearState = ClearState.Idle;
        await InvokeAsync(StateHasChanged);
    }
}
