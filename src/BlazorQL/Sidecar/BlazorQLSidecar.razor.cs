namespace BlazorQL;

/// <summary>
/// The debug sidecar panel: lists the requests <see cref="SidecarFetcher"/> has captured, toggled
/// by <see cref="SidecarOptions.ToggleShortcut"/>. Rendered once, anywhere on the page. Renders
/// nothing while closed.
/// </summary>
public partial class BlazorQLSidecar :
    IAsyncDisposable
{
    bool open;
    bool toggleButton;
    int selectedId;
    IJSObjectReference? module;
    DotNetObjectReference<BlazorQLSidecar>? reference;

    SidecarEntry? Selected =>
        Store.Entries.FirstOrDefault(_ => _.Id == selectedId);

    protected override void OnInitialized() =>
        Store.Changed += OnChanged;

    void OnChanged() =>
        InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Disabled means fully inert: no module, no key listener, nothing rendered — the same page
        // the app would show without the sidecar.
        if (!firstRender || !Store.Options.Enabled)
        {
            return;
        }

        // The button is contextual — the option is a predicate so an app can key it off the
        // current user. Decided once, here; an answer that should change mid-session belongs on
        // the markup instead (render <BlazorQLSidecar /> inside the condition).
        toggleButton = await Store.Options.ToggleButton(Services);

        reference = DotNetObjectReference.Create(this);
        module = await JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/BlazorQL/Sidecar/BlazorQLSidecar.razor.js");
        await module.InvokeVoidAsync("init", reference, Store.Options.ToggleShortcut, toggleButton);
        if (toggleButton)
        {
            StateHasChanged();
        }
    }

    [JSInvokable]
    public Task Toggle() =>
        InvokeAsync(() =>
        {
            open = !open;
            StateHasChanged();
        });

    void Select(int id) =>
        selectedId = id;

    void Clear()
    {
        Store.Clear();
        selectedId = 0;
    }

    async Task Copy(string text)
    {
        if (module is not null)
        {
            await module.InvokeVoidAsync("copy", text);
        }
    }

    /// <summary>
    /// The IDE deep link for a captured request: the query and variables carried the way the
    /// IDE's own Share does — base64url in the fragment, which never reaches a server, and which
    /// by construction cannot carry headers. Null when the route option is null.
    /// </summary>
    internal static string? IdeHref(SidecarEntry entry, string? route)
    {
        if (route is null)
        {
            return null;
        }

        var fragment = ShareLinkCodec.Encode(new(entry.Query, entry.VariablesJson ?? ""));
        return $"{route}#{fragment}";
    }

    /// <summary>The list row's state cell: live, ok, stopped, or an error marker.</summary>
    static string State(SidecarEntry entry)
    {
        if (!entry.Completed)
        {
            return "live";
        }

        if (entry.Error is not null)
        {
            return "error";
        }

        return entry.Cancelled
            ? "stopped"
            : "ok";
    }

    static string? Marker(SidecarEntry entry) =>
        entry is {Completed: true, Error: not null}
            ? "blazorql-sidecar-status-error"
            : null;

    public async ValueTask DisposeAsync()
    {
        Store.Changed -= OnChanged;
        reference?.Dispose();
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("dispose");
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is gone, and its listener with it.
            }
        }
    }
}
