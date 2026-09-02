using Microsoft.AspNetCore.Components.Web;

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

    ElementReference panel;

    Task OnKeyDown(KeyboardEventArgs args) =>
        args.Key == "Escape"
            ? OnClose.InvokeAsync()
            : Task.CompletedTask;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await panel.FocusAsync();
        }
    }
}
