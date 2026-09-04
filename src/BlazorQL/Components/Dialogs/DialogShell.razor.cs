namespace BlazorQL;

/// <summary>
/// The shared dialog chrome: a fixed overlay with a centered panel. Overlay click and Escape
/// close; clicks inside the panel stay inside. The panel takes focus on open so Escape lands
/// without a preceding click.
/// </summary>
public partial class DialogShell
{
    [Parameter]
    [EditorRequired]
    public string Title { get; set; } = "";

    [Parameter]
    public string? TestId { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// False leaves the opening focus to the content. A dialog whose first control is a text field
    /// focuses that field itself, and two focus calls on one render is a race worth not having. The
    /// panel keeps its tabindex either way, so Escape still lands once anything inside it is focused.
    /// </summary>
    [Parameter]
    public bool FocusPanel { get; set; } = true;

    ElementReference panel;

    Task OnKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            return OnClose.InvokeAsync();
        }

        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender &&
            FocusPanel)
        {
            await panel.FocusAsync();
        }
    }
}
