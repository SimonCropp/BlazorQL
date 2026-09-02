namespace BlazorQL;

/// <summary>
/// The short keys dialog: the IDE's own shortcuts. Monaco's keybindings are not listed — the
/// editors carry the full set in their command palette.
/// </summary>
public partial class ShortKeysDialog
{
    static readonly (string Keys, string Function)[] shortKeys =
    [
        ("Ctrl-Enter", "Execute query"),
        ("Shift-Ctrl-P", "Prettify editors"),
        ("Shift-Ctrl-C", "Copy query"),
        ("Shift-Ctrl-M", "Merge fragments"),
        ("Shift-Ctrl-R", "Re-fetch schema"),
        ("Ctrl-,", "Open settings dialog"),
        ("Ctrl-F", "Search in editor"),
        ("F1", "Open command palette"),
        ("Ctrl-Alt-K", "Search in documentation")
    ];

    [Parameter]
    public EventCallback OnClose { get; set; }
}
