/// <summary>
/// One document-level shortcut, in the shape <c>registerGlobalShortcuts</c> in blazorql.js reads.
/// These are the commands that live outside any editor, so Monaco's own keybindings cannot carry
/// them.
/// </summary>
sealed record Shortcut(string Id, string Key, bool Ctrl, bool Shift, bool Alt, bool Meta);
