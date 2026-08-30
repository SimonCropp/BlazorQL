using BlazorMonaco.Editor;

/// <summary>
/// GraphiQL's shared editor construction defaults (create-editor.ts), where BlazorMonaco supports
/// them: tabIndex -1 keeps editors out of the tab order (their wrappers are focusable instead).
/// </summary>
static class EditorDefaults
{
    public static StandaloneEditorConstructionOptions Create(string language, string value, string? theme) =>
        new()
        {
            Language = language,
            Value = value,
            Theme = theme,
            AutomaticLayout = true,
            FontSize = 15,
            TabSize = 2,
            Minimap = new()
            {
                Enabled = false
            },
            StickyScroll = new()
            {
                Enabled = false
            },
            RenderLineHighlight = "none",
            OverviewRulerLanes = 0,
            ScrollBeyondLastLine = false,
            LineNumbersMinChars = 2,
            RoundedSelection = false,
            Scrollbar = new()
            {
                VerticalScrollbarSize = 10
            },
            TabIndex = -1
        };
}
