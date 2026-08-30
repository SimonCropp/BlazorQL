using System.Diagnostics;
using System.Globalization;

namespace BlazorQL;

/// <summary>
/// The BlazorQL IDE. Renders the editor shell and drives the vendored Monaco/monaco-graphql stack
/// through the <c>blazorql.js</c> host module. One instance per page.
/// </summary>
public partial class BlazorQLIde :
    IAsyncDisposable
{
    internal const string OperationElementId = "blazorql-operation-editor";
    internal const string ResponseElementId = "blazorql-response-editor";
    const string OperationUri = "operation.graphql";
    const string ResponseUri = "response.json";
    const string VariablesUri = "variables.json";
    const string HeadersUri = "request-headers.json";
    const string PluginResizerId = "blazorql-plugin-resizer";
    const string SessionResizerId = "blazorql-session-resizer";
    const string ToolsResizerId = "blazorql-tools-resizer";
    const double CollapseThreshold = 100;

    /// <summary>Transports requests — including the introspection the schema is built from.</summary>
    [Parameter]
    [EditorRequired]
    public IGraphQLFetcher Fetcher { get; set; } = null!;

    /// <summary>Seed for the operation editor. Null renders the welcome text.</summary>
    [Parameter]
    public string? DefaultQuery { get; set; }

    /// <summary>Seed for the headers editor, applied to the initial tab and every new tab.</summary>
    [Parameter]
    public string? DefaultHeaders { get; set; }

    /// <summary>False hides the Headers tool and never creates its editor.</summary>
    [Parameter]
    public bool IsHeadersEditorEnabled { get; set; } = true;

    /// <summary>
    /// Whether headers persist across reloads. A value the user chose in the settings dialog is
    /// stored and wins over this parameter on the next boot.
    /// </summary>
    [Parameter]
    public bool ShouldPersistHeaders { get; set; }

    /// <summary>How many non-favorite history items are kept before the oldest is evicted.</summary>
    [Parameter]
    public int MaxHistoryLength { get; set; } = 20;

    /// <summary>Prefix for every localStorage key this instance writes.</summary>
    [Parameter]
    public string StorageNamespace { get; set; } = "blazorql";

    /// <summary>Pins the theme, overriding the user's toggle. Null leaves the toggle in charge.</summary>
    [Parameter]
    public Theme? ForcedTheme { get; set; }

    /// <summary>The theme preference before the user touches the toggle.</summary>
    [Parameter]
    public Theme DefaultTheme { get; set; } = Theme.System;

    /// <summary>Replaces the default BlazorQL logo in the session header.</summary>
    [Parameter]
    public RenderFragment? Logo { get; set; }

    /// <summary>Extra buttons rendered under the execute button in the editor toolbar.</summary>
    [Parameter]
    public RenderFragment? ToolbarContent { get; set; }

    /// <summary>Rendered as a footer under the response pane.</summary>
    [Parameter]
    public RenderFragment? FooterContent { get; set; }

    /// <summary>Asked before a tab closes, with its index. Return false to keep the tab.</summary>
    [Parameter]
    public Func<int, Task<bool>>? ConfirmCloseTab { get; set; }

    /// <summary>Fires after the schema is introspected and pushed to the editors.</summary>
    [Parameter]
    public EventCallback OnSchemaLoaded { get; set; }

    bool ready;
    bool running;
    bool refetching;
    JsModule? module;
    readonly BlazorQLCallbacks callbacks = new();
    DotNetObjectReference<BlazorQLCallbacks>? reference;
    CancellationTokenSource? execution;

    // Shell state. Pane ratios are the first pane's share of the container (see PaneState);
    // persistence of all of this arrives in M6.
    readonly TabStore tabs = new();
    readonly ThemeService themes = new();
    readonly PaneState pluginPane = new(1.0 / 3);
    readonly PaneState sessionPane = new(0.5);
    // The operation editor's share of the editors column; 3:1 over the editor tools.
    readonly PaneState toolsPane = new(0.75);
    PluginKind? visiblePlugin;
    readonly DocExplorerNavigator docNavigator = new();
    bool toolsExpanded;
    EditorTool activeTool = EditorTool.Variables;
    bool pickerOpen;
    IReadOnlyList<OperationInfo> pickerOperations = [];

    // M6 persistence: storage + history exist only after the host module is imported (the storage
    // backend is the module's localStorage seam). Writes are debounced so typing does not thrash
    // localStorage.
    StorageService? storage;
    HistoryStore? history;
    bool persistHeaders;
    bool settingsOpen;
    bool shortKeysOpen;

    // M7: the status footer under the response pane, and the pending focus request the Ctrl-Alt-K
    // shortcut leaves for the render that opens the docs pane.
    string? statusLine;
    bool docSearchFocusPending;
    readonly Debouncer stateDebounce = new();
    readonly Debouncer paneDebounce = new();

    /// <summary>The schema printed as SDL, once loaded.</summary>
    public string? SchemaSdl { get; private set; }

    /// <summary>The parsed introspection result, once loaded — what the doc explorer navigates.</summary>
    public SchemaIndex? Schema { get; private set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            // The docs pane (and its search input) had to render before it could take focus.
            if (docSearchFocusPending)
            {
                docSearchFocusPending = false;
                await module!.Invoke("focusElement", "#blazorql-doc-search-input");
            }

            return;
        }

        module = new(JS);
        reference = DotNetObjectReference.Create(callbacks);
        callbacks.EditorAction += OnEditorAction;
        callbacks.EditorChanged += OnEditorChanged;
        callbacks.PaneResize += OnPaneResize;
        callbacks.SchemaReference += OnSchemaReference;
        callbacks.GlobalShortcut += OnGlobalShortcut;
        if (Fetcher is LocalSchemaFetcher local)
        {
            local.Attach(module, callbacks);
        }

        await module.Invoke<JsonElement>("init", reference, "blazorql");

        // Storage rides the freshly imported module, so everything persisted is rehydrated here —
        // before the editors take their initial values.
        storage = new(new JsStorageBackend(module), StorageNamespace);
        history = new(storage, QueryParses, MaxHistoryLength);
        Rehydrate();
        // A #q= share link wins over storage for the active tab's query and variables.
        await ApplySharedLink();

        await ApplyTheme();
        await module.Invoke(
            "createEditor",
            OperationElementId,
            OperationUri,
            "graphql",
            tabs.Active.Query,
            null);
        await module.Invoke(
            "createEditor",
            ResponseElementId,
            ResponseUri,
            "json",
            "",
            """{"readOnly": true, "lineNumbers": "off", "wordWrap": "on", "contextmenu": false}""");
        await module.Invoke(
            "createEditor",
            EditorTools.VariablesElementId,
            VariablesUri,
            "json",
            tabs.Active.Variables,
            null);
        if (IsHeadersEditorEnabled)
        {
            await module.Invoke(
                "createEditor",
                EditorTools.HeadersElementId,
                HeadersUri,
                "json",
                tabs.Active.Headers,
                null);
        }

        // Monaco KeyMod.CtrlCmd | KeyCode.Enter.
        await module.Invoke("addAction", OperationUri, "blazorql-run", "Run Operation", "[2051]");
        // GraphiQL's editor bindings: Shift-Ctrl-P prettify, Shift-Ctrl-M merge, Shift-Ctrl-C copy
        // (KeyMod.Shift | KeyMod.WinCtrl | the key).
        await module.Invoke("addAction", OperationUri, "blazorql-prettify", "Prettify Editors", "[1326]");
        await module.Invoke("addAction", OperationUri, "blazorql-merge", "Merge Fragments", "[1323]");
        await module.Invoke("addAction", OperationUri, "blazorql-copy", "Copy Query", "[1313]");
        await module.Invoke("addAction", VariablesUri, "blazorql-prettify", "Prettify Editors", "[1326]");
        if (IsHeadersEditorEnabled)
        {
            await module.Invoke("addAction", HeadersUri, "blazorql-prettify", "Prettify Editors", "[1326]");
        }

        // Keeps the active tab's Query (and so its derived title) in step with typing.
        await module.Invoke("onChange", OperationUri, 300);
        // The other editors feed the active tab too, so edits persist without a tab switch.
        await module.Invoke("onChange", VariablesUri, 500);
        if (IsHeadersEditorEnabled)
        {
            await module.Invoke("onChange", HeadersUri, 500);
        }

        await module.Invoke("onChange", ResponseUri, 500);
        // Ctrl/Cmd+click on a schema name jumps to its documentation.
        await module.Invoke("registerJumpToDoc", OperationUri);
        // Hovering an image url in a response previews the image.
        await module.Invoke("registerResponseImageHover", ResponseUri);
        // Document-level shortcuts for commands that live outside any editor.
        await module.Invoke(
            "registerGlobalShortcuts",
            JsonSerializer.Serialize(new object[]
            {
                new {id = "refetch", key = "r", ctrl = true, shift = true, alt = false, meta = false},
                new {id = "doc-search", key = "k", ctrl = true, shift = false, alt = true, meta = false},
                new {id = "settings", key = ",", ctrl = true, shift = false, alt = false, meta = false}
            }));

        await module.Invoke("trackPointer", PluginResizerId, "plugin", "x");
        await module.Invoke("trackPointer", SessionResizerId, "session", "x");
        await module.Invoke("trackPointer", ToolsResizerId, "tools", "y");

        await LoadSchema();

        ready = true;
        StateHasChanged();
    }

    // ---- Persistence ----

    /// <summary>Restores everything M6 persists, seeding defaults where storage is empty.</summary>
    void Rehydrate()
    {
        // The stored persist-headers choice wins over the parameter; absent, the parameter decides.
        persistHeaders = storage!.Get("shouldPersistHeaders") is { } storedPersist
            ? storedPersist == "true"
            : ShouldPersistHeaders;

        themes.Current = storage.Get("theme") switch
        {
            "light" => Theme.Light,
            "dark" => Theme.Dark,
            _ => DefaultTheme
        };

        visiblePlugin = storage.Get("visiblePlugin") switch
        {
            "docs" => PluginKind.Docs,
            "history" => PluginKind.History,
            _ => null
        };

        RestorePane(pluginPane, "docExplorerFlex");
        RestorePane(sessionPane, "editorFlex");
        RestorePane(toolsPane, "secondaryEditorFlex");

        if (!tabs.TryRestore(storage.Get("tabState")))
        {
            // Nothing usable stored: the very first tab is the only one seeded with the welcome text.
            tabs.Add(DefaultQuery ?? WelcomeQuery, DefaultHeaders ?? "");
        }

        // Tools open when a tab has content for them, unless the user had collapsed the strip.
        toolsExpanded =
            storage.Get("secondaryEditorFlex") != "collapsed" &&
            (!string.IsNullOrWhiteSpace(tabs.Active.Variables) ||
             (IsHeadersEditorEnabled && !string.IsNullOrWhiteSpace(tabs.Active.Headers)));
    }

    /// <summary>
    /// Loads a <c>#q=</c> share link into the active tab, before the editors take their initial
    /// values. A malformed fragment is ignored silently.
    /// </summary>
    async Task ApplySharedLink()
    {
        var hash = await module!.Invoke<string>("getHash");
        if (ShareLinkCodec.TryDecode(hash) is not { } shared)
        {
            return;
        }

        var tab = tabs.Active;
        tab.Query = shared.Query;
        tab.Variables = shared.Variables;
        if (!string.IsNullOrWhiteSpace(shared.Variables))
        {
            toolsExpanded = true;
        }
    }

    void RestorePane(PaneState pane, string key)
    {
        var value = storage!.Get(key);
        if (value is not null &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) &&
            ratio is > 0 and < 1)
        {
            pane.Ratio = ratio;
        }
    }

    /// <summary>Whether a query parses as GraphQL — the history's gate against garbage entries.</summary>
    bool QueryParses(string query) =>
        module!.InvokeSync<string?>("getOperationFacts", query) is not null;

    void SchedulePersist() =>
        stateDebounce.Run(() =>
        {
            PersistState();
            return Task.CompletedTask;
        });

    /// <summary>Writes the tab state plus GraphiQL's flat mirrors of the active tab.</summary>
    void PersistState()
    {
        if (storage is null)
        {
            return;
        }

        storage.Set("tabState", tabs.Serialize(persistHeaders));
        var tab = tabs.Active;
        storage.Set("query", tab.Query);
        storage.Set("variables", tab.Variables);
        if (persistHeaders)
        {
            storage.Set("headers", tab.Headers);
        }
    }

    void SchedulePersistPanes() =>
        paneDebounce.Run(() =>
        {
            if (storage is not null)
            {
                storage.Set("docExplorerFlex", FormatRatio(pluginPane.Ratio));
                storage.Set("editorFlex", FormatRatio(sessionPane.Ratio));
                storage.Set(
                    "secondaryEditorFlex",
                    toolsExpanded
                        ? FormatRatio(toolsPane.Ratio)
                        : "collapsed");
            }

            return Task.CompletedTask;
        });

    static string FormatRatio(double ratio) =>
        ratio.ToString("0.####", CultureInfo.InvariantCulture);

    void PersistVisiblePlugin()
    {
        if (storage is null)
        {
            return;
        }

        switch (visiblePlugin)
        {
            case PluginKind.Docs:
                storage.Set("visiblePlugin", "docs");
                break;
            case PluginKind.History:
                storage.Set("visiblePlugin", "history");
                break;
            default:
                storage.Remove("visiblePlugin");
                break;
        }
    }

    void PersistTheme()
    {
        if (storage is null)
        {
            return;
        }

        switch (themes.Current)
        {
            case Theme.Light:
                storage.Set("theme", "light");
                break;
            case Theme.Dark:
                storage.Set("theme", "dark");
                break;
            default:
                // Absent = follow the system.
                storage.Remove("theme");
                break;
        }
    }

    /// <summary>
    /// The settings dialog's persist-headers switch. Turning on snapshots the current headers;
    /// turning off scrubs every stored header.
    /// </summary>
    async Task SetPersistHeaders(bool value)
    {
        if (persistHeaders == value)
        {
            return;
        }

        persistHeaders = value;
        storage?.Set("shouldPersistHeaders", value ? "true" : "false");
        if (value)
        {
            // Snapshot what the headers editor holds right now.
            await SaveActiveTab();
        }
        else
        {
            storage?.Remove("headers");
        }

        // Rewrites tabState — with headers included, or nulled out.
        PersistState();
    }

    bool ClearStorage()
    {
        try
        {
            storage?.Clear();
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }

    async Task LoadSchema()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            JsonElement? introspection = null;
            await foreach (var payload in Fetcher.FetchAsync(new(IntrospectionQuery), EmptyHeaders, cts.Token))
            {
                introspection = payload;
                break;
            }

            if (introspection is null)
            {
                await SetResponse("""{"errors":[{"message":"Introspection returned no result."}]}""");
                return;
            }

            SchemaSdl = await module!.Invoke<string>("setSchemaFromIntrospection", introspection.Value.GetRawText());
            Schema = SchemaIndex.Parse(introspection.Value);
            // With a schema in place the variables editor can validate against the operation's
            // declared variables.
            await module.Invoke("linkVariablesValidation", OperationUri, VariablesUri);
            await OnSchemaLoaded.InvokeAsync();
        }
        catch (Exception exception)
        {
            await SetResponse(JsonSerializer.Serialize(new
            {
                errors = new[] {new {message = $"Introspection failed: {exception.Message}"}}
            }));
        }
    }

    async Task RefetchSchema()
    {
        refetching = true;
        StateHasChanged();
        try
        {
            await LoadSchema();
        }
        finally
        {
            refetching = false;
            StateHasChanged();
        }
    }

    // ---- Theme ----

    async Task CycleTheme()
    {
        themes.Cycle();
        PersistTheme();
        await ApplyTheme();
    }

    /// <summary>The settings dialog's explicit theme choice — same service as the sidebar cycle.</summary>
    async Task SelectTheme(Theme theme)
    {
        themes.Current = theme;
        PersistTheme();
        await ApplyTheme();
    }

    async Task ApplyTheme()
    {
        var effective = ForcedTheme ?? themes.Current;
        var systemDark = effective == Theme.System && await module!.Invoke<bool>("systemDark");
        var mode = effective == Theme.Dark || systemDark
            ? "dark"
            : "light";
        await module!.Invoke("setDataTheme", mode);
        await module.Invoke("setMonacoTheme", $"blazorql-{mode}");
    }

    // ---- Plugin pane ----

    void TogglePlugin(PluginKind plugin)
    {
        visiblePlugin = visiblePlugin == plugin
            ? null
            : plugin;
        PersistVisiblePlugin();
    }

    /// <summary>Jump-to-doc: opens the docs plugin and navigates to the referenced schema member.</summary>
    void OnSchemaReference(string referenceJson) =>
        _ = InvokeAsync(() =>
        {
            var reference = JsonSerializer.Deserialize<SchemaReference>(referenceJson, referenceOptions);
            if (reference is null)
            {
                return;
            }

            visiblePlugin = PluginKind.Docs;
            docNavigator.NavigateTo(reference);
            StateHasChanged();
        });

    static readonly JsonSerializerOptions referenceOptions = new(JsonSerializerDefaults.Web);

    // ---- Editor tools ----

    void SelectTool(EditorTool tool)
    {
        if (toolsExpanded && activeTool == tool)
        {
            toolsExpanded = false;
            SchedulePersistPanes();
            return;
        }

        activeTool = tool;
        toolsExpanded = true;
        SchedulePersistPanes();
    }

    void ToggleTools()
    {
        toolsExpanded = !toolsExpanded;
        SchedulePersistPanes();
    }

    void ResetToolsPane()
    {
        toolsPane.Reset();
        toolsExpanded = true;
        SchedulePersistPanes();
    }

    // ---- Pane resizing ----

    void OnPaneResize(string resizerId, double fraction, double size) =>
        _ = InvokeAsync(() =>
        {
            var pixels = fraction * size;
            switch (resizerId)
            {
                case "plugin":
                    if (pixels < CollapseThreshold)
                    {
                        // Collapsing the plugin pane closes the plugin.
                        visiblePlugin = null;
                        pluginPane.Reset();
                        PersistVisiblePlugin();
                    }
                    else
                    {
                        pluginPane.Ratio = Math.Clamp(fraction, 0.1, 0.8);
                    }

                    break;
                case "session":
                    sessionPane.Ratio = Math.Clamp(fraction, 0.1, 0.9);
                    break;
                case "tools":
                    if (size - pixels < CollapseThreshold)
                    {
                        toolsExpanded = false;
                    }
                    else
                    {
                        toolsExpanded = true;
                        toolsPane.Ratio = Math.Clamp(fraction, 0.15, 0.95);
                    }

                    break;
            }

            SchedulePersistPanes();
            StateHasChanged();
        });

    static void ResetPane(PaneState pane) =>
        pane.Reset();

    static string Grow(double ratio) =>
        $"flex: {ratio.ToString("0.####", CultureInfo.InvariantCulture)} 1 0%";

    // ---- Tabs ----

    async Task ActivateTab(int index)
    {
        if (index == tabs.ActiveIndex)
        {
            return;
        }

        // A run in flight belongs to the old tab; stop it before the editors change hands.
        execution?.Cancel();
        pickerOpen = false;
        await SaveActiveTab();
        tabs.Activate(index);
        await LoadActiveTab();
        SchedulePersist();
    }

    async Task AddTab()
    {
        execution?.Cancel();
        pickerOpen = false;
        await SaveActiveTab();
        // New tabs start empty; only the very first default tab carries the welcome text.
        tabs.Add("", DefaultHeaders ?? "");
        await LoadActiveTab();
        SchedulePersist();
    }

    async Task CloseTab(int index)
    {
        if (ConfirmCloseTab is not null && !await ConfirmCloseTab(index))
        {
            return;
        }

        var closingActive = index == tabs.ActiveIndex;
        if (closingActive)
        {
            execution?.Cancel();
            pickerOpen = false;
        }

        tabs.Close(index);
        if (closingActive)
        {
            await LoadActiveTab();
        }

        SchedulePersist();
    }

    void RenameTab((int Index, string? Title) rename)
    {
        tabs.Tabs[rename.Index].RenameOverride = rename.Title;
        SchedulePersist();
    }

    async Task SaveActiveTab()
    {
        var tab = tabs.Active;
        tab.Query = await module!.Invoke<string>("getValue", OperationUri);
        tab.Variables = await module.Invoke<string>("getValue", VariablesUri);
        if (IsHeadersEditorEnabled)
        {
            tab.Headers = await module.Invoke<string>("getValue", HeadersUri);
        }

        tab.Response = await module.Invoke<string>("getValue", ResponseUri);
    }

    async Task LoadActiveTab()
    {
        var tab = tabs.Active;
        await module!.Invoke("setValue", OperationUri, tab.Query);
        await module.Invoke("setValue", VariablesUri, tab.Variables);
        if (IsHeadersEditorEnabled)
        {
            await module.Invoke("setValue", HeadersUri, tab.Headers);
        }

        await module.Invoke("setValue", ResponseUri, tab.Response);
        // The status line described the previous tab's run.
        statusLine = null;
        // Tools open themselves for a tab that has content in them, close otherwise.
        toolsExpanded =
            !string.IsNullOrWhiteSpace(tab.Variables) ||
            (IsHeadersEditorEnabled && !string.IsNullOrWhiteSpace(tab.Headers));
    }

    void OnEditorChanged(string uriName, string text) =>
        _ = InvokeAsync(() =>
        {
            // Every editor routes through the active tab, so its state (and the persisted
            // tabState) stays current without waiting for a tab switch.
            var tab = tabs.Active;
            switch (uriName)
            {
                case OperationUri:
                    // Also keeps the derived tab title live while the user types.
                    tab.Query = text;
                    break;
                case VariablesUri:
                    tab.Variables = text;
                    break;
                case HeadersUri:
                    tab.Headers = text;
                    break;
                case ResponseUri:
                    // Tracked for tab switches, but never persisted. The response-action buttons
                    // key off this value, so the change still renders.
                    tab.Response = text;
                    StateHasChanged();
                    return;
                default:
                    return;
            }

            SchedulePersist();
            StateHasChanged();
        });

    // ---- Execution ----

    void OnEditorAction(string actionId)
    {
        switch (actionId)
        {
            case "blazorql-run":
                _ = InvokeAsync(RunFromKeyboard);
                break;
            case "blazorql-prettify":
                _ = InvokeAsync(PrettifyEditors);
                break;
            case "blazorql-merge":
                _ = InvokeAsync(MergeFragments);
                break;
            case "blazorql-copy":
                _ = InvokeAsync(CopyQuery);
                break;
        }
    }

    /// <summary>Document-level shortcuts registered with the host module.</summary>
    void OnGlobalShortcut(string id) =>
        _ = InvokeAsync(async () =>
        {
            switch (id)
            {
                case "refetch":
                    if (!refetching)
                    {
                        await RefetchSchema();
                    }

                    break;
                case "doc-search":
                    visiblePlugin = PluginKind.Docs;
                    PersistVisiblePlugin();
                    // Focus lands after the pane has rendered — see OnAfterRenderAsync.
                    docSearchFocusPending = true;
                    StateHasChanged();
                    break;
                case "settings":
                    settingsOpen = !settingsOpen;
                    StateHasChanged();
                    break;
            }
        });

    // ---- Toolbar operations ----

    /// <summary>Prettifies every editor, in GraphiQL's order: variables, headers, then the query.</summary>
    async Task PrettifyEditors()
    {
        await module!.Invoke("prettify", VariablesUri);
        if (IsHeadersEditorEnabled)
        {
            await module.Invoke("prettify", HeadersUri);
        }

        await module.Invoke("prettify", OperationUri);
    }

    /// <summary>Inlines named fragments into the operations. A parse failure becomes the response.</summary>
    async Task MergeFragments()
    {
        var resultJson = await module!.Invoke<string>("mergeFragments", OperationUri);
        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
        {
            await SetResponse(ErrorDocument(root.GetProperty("error").GetString() ?? "Merge failed."));
        }
    }

    async Task CopyQuery()
    {
        var query = await module!.Invoke<string>("getValue", OperationUri);
        await module.Invoke("copyText", query);
    }

    /// <summary>Writes the query + variables into the location hash and copies the resulting link.</summary>
    async Task ShareQuery()
    {
        var shared = new SharedQuery(
            await module!.Invoke<string>("getValue", OperationUri),
            await module.Invoke<string>("getValue", VariablesUri));
        var href = await module.Invoke<string>("setHash", ShareLinkCodec.Encode(shared));
        await module.Invoke("copyText", href);
    }

    async Task CopyResponse()
    {
        var response = await module!.Invoke<string>("getValue", ResponseUri);
        await module.Invoke("copyText", response);
    }

    async Task DownloadResponse()
    {
        var response = await module!.Invoke<string>("getValue", ResponseUri);
        await module.Invoke("downloadText", "response.json", response, "application/json");
    }

    /// <summary>Ctrl-Enter: with several operations in the document the caret decides.</summary>
    async Task RunFromKeyboard()
    {
        if (running)
        {
            execution?.Cancel();
            return;
        }

        var query = await module!.Invoke<string>("getValue", OperationUri);
        var operations = await GetOperations(query);
        string? operationName = null;
        if (operations.Count > 1)
        {
            var offset = await module.Invoke<int>("getCursorOffset", OperationUri);
            var at = operations.FirstOrDefault(_ => _.Start <= offset && offset <= _.End);
            operationName = (at ?? operations[0]).Name;
        }
        else if (operations.Count == 1)
        {
            operationName = operations[0].Name;
        }

        await Run(query, operationName, operations.Count > 1);
    }

    /// <summary>The execute button: one operation runs it; several open the picker.</summary>
    async Task ExecuteClicked()
    {
        if (running)
        {
            execution?.Cancel();
            return;
        }

        if (pickerOpen)
        {
            pickerOpen = false;
            return;
        }

        var query = await module!.Invoke<string>("getValue", OperationUri);
        var operations = await GetOperations(query);
        if (operations.Count > 1)
        {
            pickerOperations = operations;
            pickerOpen = true;
            return;
        }

        await Run(query, operations.Count == 1 ? operations[0].Name : null, multipleOperations: false);
    }

    async Task RunPicked(OperationInfo operation)
    {
        pickerOpen = false;
        var query = await module!.Invoke<string>("getValue", OperationUri);
        await Run(query, operation.Name, multipleOperations: true);
    }

    async Task Run(string query, string? operationName, bool multipleOperations)
    {
        // Fill in default leaf selections first; the filled text is what runs (and what the user
        // sees, briefly highlighted).
        query = await module!.Invoke<string>("fillLeafs", OperationUri);

        // The parse errors short-circuit: nothing is sent, the error is the response.
        var variables = await ParseJsonc(VariablesUri, "Variables");
        if (variables.Error is not null)
        {
            await SetResponse(ErrorDocument(variables.Error));
            return;
        }

        var headers = EmptyHeaders;
        if (IsHeadersEditorEnabled)
        {
            var parsedHeaders = await ParseJsonc(HeadersUri, "Request headers");
            if (parsedHeaders.Error is not null)
            {
                await SetResponse(ErrorDocument(parsedHeaders.Error));
                return;
            }

            headers = ToHeaderDictionary(parsedHeaders.Value);
        }

        // The operation actually run names the tab (only meaningful when the caret or picker had
        // to disambiguate).
        if (multipleOperations)
        {
            tabs.Active.OperationName = operationName;
            SchedulePersist();
        }

        // The history records every execution start, whether or not its pane is open.
        var variablesText = await module!.Invoke<string>("getValue", VariablesUri);
        var headersText = IsHeadersEditorEnabled
            ? await module.Invoke<string>("getValue", HeadersUri)
            : "";
        history?.Record(
            query,
            string.IsNullOrWhiteSpace(variablesText) ? null : variablesText,
            string.IsNullOrWhiteSpace(headersText) ? null : headersText,
            operationName);

        execution = new();
        running = true;
        StateHasChanged();

        var merger = new IncrementalMerger();
        // Elapsed covers the full fetch; the status text is what the footer line shows. HTTP
        // status codes arrive with the HTTP fetcher (M8) — until then "OK" stands in for success.
        var stopwatch = Stopwatch.StartNew();
        var status = "OK";
        try
        {
            await foreach (var payload in Fetcher.FetchAsync(new(query, variables.Value, operationName), headers, execution.Token))
            {
                merger.Add(payload);
                await SetResponse(merger.Render());
            }

            if (merger.HasErrors)
            {
                status = "error";
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user; whatever arrived stays on screen.
            status = "stopped";
        }
        catch (Exception exception)
        {
            status = "error";
            await SetResponse(JsonSerializer.Serialize(new
            {
                errors = new[] {new {message = exception.Message}}
            }));
        }
        finally
        {
            stopwatch.Stop();
            statusLine = $"{status} · {stopwatch.ElapsedMilliseconds} ms";
            execution.Dispose();
            execution = null;
            running = false;
            StateHasChanged();
        }
    }

    async Task<IReadOnlyList<OperationInfo>> GetOperations(string query)
    {
        var factsJson = await module!.Invoke<string?>("getOperationFacts", query);
        if (factsJson is null)
        {
            return [];
        }

        using var document = JsonDocument.Parse(factsJson);
        List<OperationInfo> operations = [];
        foreach (var element in document.RootElement.GetProperty("operations").EnumerateArray())
        {
            operations.Add(new(
                element.GetProperty("name").ValueKind == JsonValueKind.String
                    ? element.GetProperty("name").GetString()
                    : null,
                element.GetProperty("operation").GetString() ?? "query",
                element.GetProperty("start").GetInt32(),
                element.GetProperty("end").GetInt32()));
        }

        return operations;
    }

    /// <summary>Runs the given editor's content through the host module's JSONC parser.</summary>
    async Task<(JsonElement? Value, string? Error)> ParseJsonc(string uriName, string what)
    {
        var text = await module!.Invoke<string>("getValue", uriName);
        var resultJson = await module.Invoke<string>("parseJsonc", text, what);
        using var document = JsonDocument.Parse(resultJson);
        var root = document.RootElement;
        if (!root.GetProperty("ok").GetBoolean())
        {
            return (null, root.GetProperty("error").GetString());
        }

        if (root.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return (value.Clone(), null);
        }

        return (null, null);
    }

    static string ErrorDocument(string message) =>
        JsonSerializer.Serialize(new
        {
            errors = new[] {new {message}}
        });

    static Dictionary<string, string> ToHeaderDictionary(JsonElement? parsed)
    {
        Dictionary<string, string> headers = [];
        if (parsed is not {ValueKind: JsonValueKind.Object} element)
        {
            return headers;
        }

        foreach (var property in element.EnumerateObject())
        {
            headers[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()!
                : property.Value.GetRawText();
        }

        return headers;
    }

    ValueTask SetResponse(string text) =>
        module!.Invoke("setValue", ResponseUri, text);

    /// <summary>Whether the response pane shows anything — gates the copy/download overlay.</summary>
    bool HasResponse =>
        ready && !string.IsNullOrWhiteSpace(tabs.Active.Response);

    // ---- History + dialogs ----

    /// <summary>Clicking a history item loads it into the active tab's editors.</summary>
    async Task LoadHistoryItem(HistoryItem item)
    {
        var tab = tabs.Active;
        tab.Query = item.Query;
        tab.Variables = item.Variables ?? "";
        if (IsHeadersEditorEnabled && item.Headers is not null)
        {
            tab.Headers = item.Headers;
        }

        await module!.Invoke("setValue", OperationUri, tab.Query);
        await module.Invoke("setValue", VariablesUri, tab.Variables);
        if (IsHeadersEditorEnabled)
        {
            await module.Invoke("setValue", HeadersUri, tab.Headers);
        }

        SchedulePersist();
    }

    void OpenSettings() =>
        settingsOpen = true;

    void CloseSettings() =>
        settingsOpen = false;

    void OpenShortKeys() =>
        shortKeysOpen = true;

    void CloseShortKeys() =>
        shortKeysOpen = false;

    static readonly Dictionary<string, string> EmptyHeaders = [];

    public async ValueTask DisposeAsync()
    {
        callbacks.EditorAction -= OnEditorAction;
        callbacks.EditorChanged -= OnEditorChanged;
        callbacks.PaneResize -= OnPaneResize;
        callbacks.SchemaReference -= OnSchemaReference;
        callbacks.GlobalShortcut -= OnGlobalShortcut;
        execution?.Cancel();
        stateDebounce.Dispose();
        paneDebounce.Dispose();
        reference?.Dispose();
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    /// <summary>One operation in the document, as getOperationFacts reports it.</summary>
    sealed record OperationInfo(string? Name, string Operation, int Start, int End);

    // The standard introspection query, as graphql-js emits it (descriptions and deprecated
    // members included; nine levels of type nesting).
    internal const string IntrospectionQuery =
        """
        query IntrospectionQuery {
          __schema {
            description
            queryType { name kind }
            mutationType { name kind }
            subscriptionType { name kind }
            types { ...FullType }
            directives {
              name
              description
              isRepeatable
              locations
              args(includeDeprecated: true) { ...InputValue }
            }
          }
        }

        fragment FullType on __Type {
          kind
          name
          description
          specifiedByURL
          fields(includeDeprecated: true) {
            name
            description
            args(includeDeprecated: true) { ...InputValue }
            type { ...TypeRef }
            isDeprecated
            deprecationReason
          }
          inputFields(includeDeprecated: true) { ...InputValue }
          interfaces { ...TypeRef }
          enumValues(includeDeprecated: true) {
            name
            description
            isDeprecated
            deprecationReason
          }
          possibleTypes { ...TypeRef }
        }

        fragment InputValue on __InputValue {
          name
          description
          type { ...TypeRef }
          defaultValue
          isDeprecated
          deprecationReason
        }

        fragment TypeRef on __Type {
          kind
          name
          ofType {
            kind
            name
            ofType {
              kind
              name
              ofType {
                kind
                name
                ofType {
                  kind
                  name
                  ofType {
                    kind
                    name
                    ofType {
                      kind
                      name
                      ofType {
                        kind
                        name
                        ofType {
                          kind
                          name
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

    // Adapted from GraphiQL's welcome comment, with BlazorQL's shortcut spellings.
    internal const string WelcomeQuery =
        """
        # Welcome to BlazorQL
        #
        # BlazorQL is an in-browser tool for writing, validating, and testing
        # GraphQL queries.
        #
        # Type queries into this side of the screen, and you will see intelligent
        # typeaheads aware of the current GraphQL type schema and live syntax and
        # validation errors highlighted within the text.
        #
        # GraphQL queries typically start with a "{" character. Lines that start
        # with a # are ignored.
        #
        # An example GraphQL query might look like:
        #
        #     {
        #       field(arg: "value") {
        #         subField
        #       }
        #     }
        #
        # Keyboard shortcuts:
        #
        #   Prettify query:  Shift-Ctrl-P (or press the prettify button)
        #
        #  Merge fragments:  Shift-Ctrl-M (or press the merge button)
        #
        #        Run Query:  Ctrl-Enter (or press the play button)
        #
        #    Auto Complete:  Space (or just start typing)
        #

        """;
}
