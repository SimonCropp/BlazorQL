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
    bool toolsExpanded;
    EditorTool activeTool = EditorTool.Variables;
    bool pickerOpen;
    IReadOnlyList<OperationInfo> pickerOperations = [];

    /// <summary>The schema printed as SDL, once loaded.</summary>
    public string? SchemaSdl { get; private set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        module = new(JS);
        reference = DotNetObjectReference.Create(callbacks);
        callbacks.EditorAction += OnEditorAction;
        callbacks.EditorChanged += OnEditorChanged;
        callbacks.PaneResize += OnPaneResize;
        if (Fetcher is LocalSchemaFetcher local)
        {
            local.Attach(module, callbacks);
        }

        themes.Current = DefaultTheme;

        // The very first tab is the only one seeded with the welcome text.
        tabs.Add(DefaultQuery ?? WelcomeQuery, DefaultHeaders ?? "");
        toolsExpanded = !string.IsNullOrWhiteSpace(DefaultHeaders);

        await module.Invoke<JsonElement>("init", reference, "blazorql");
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
        // Keeps the active tab's Query (and so its derived title) in step with typing.
        await module.Invoke("onChange", OperationUri, 300);

        await module.Invoke("trackPointer", PluginResizerId, "plugin", "x");
        await module.Invoke("trackPointer", SessionResizerId, "session", "x");
        await module.Invoke("trackPointer", ToolsResizerId, "tools", "y");

        await LoadSchema();

        ready = true;
        StateHasChanged();
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

    void TogglePlugin(PluginKind plugin) =>
        visiblePlugin = visiblePlugin == plugin
            ? null
            : plugin;

    // ---- Editor tools ----

    void SelectTool(EditorTool tool)
    {
        if (toolsExpanded && activeTool == tool)
        {
            toolsExpanded = false;
            return;
        }

        activeTool = tool;
        toolsExpanded = true;
    }

    void ToggleTools() =>
        toolsExpanded = !toolsExpanded;

    void ResetToolsPane()
    {
        toolsPane.Reset();
        toolsExpanded = true;
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
    }

    async Task AddTab()
    {
        execution?.Cancel();
        pickerOpen = false;
        await SaveActiveTab();
        // New tabs start empty; only the very first default tab carries the welcome text.
        tabs.Add("", DefaultHeaders ?? "");
        await LoadActiveTab();
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
    }

    void RenameTab((int Index, string? Title) rename) =>
        tabs.Tabs[rename.Index].RenameOverride = rename.Title;

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
        // Tools open themselves for a tab that has content in them, close otherwise.
        toolsExpanded =
            !string.IsNullOrWhiteSpace(tab.Variables) ||
            (IsHeadersEditorEnabled && !string.IsNullOrWhiteSpace(tab.Headers));
    }

    void OnEditorChanged(string uriName, string text)
    {
        if (uriName != OperationUri)
        {
            return;
        }

        _ = InvokeAsync(() =>
        {
            // Keeps the derived tab title live while the user types.
            tabs.Active.Query = text;
            StateHasChanged();
        });
    }

    // ---- Execution ----

    void OnEditorAction(string actionId)
    {
        if (actionId == "blazorql-run")
        {
            _ = InvokeAsync(RunFromKeyboard);
        }
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
        }

        execution = new();
        running = true;
        StateHasChanged();

        var merger = new IncrementalMerger();
        try
        {
            await foreach (var payload in Fetcher.FetchAsync(new(query, variables.Value, operationName), headers, execution.Token))
            {
                merger.Add(payload);
                await SetResponse(merger.Render());
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user; whatever arrived stays on screen.
        }
        catch (Exception exception)
        {
            await SetResponse(JsonSerializer.Serialize(new
            {
                errors = new[] {new {message = exception.Message}}
            }));
        }
        finally
        {
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

    static readonly Dictionary<string, string> EmptyHeaders = [];

    public async ValueTask DisposeAsync()
    {
        callbacks.EditorAction -= OnEditorAction;
        callbacks.EditorChanged -= OnEditorChanged;
        callbacks.PaneResize -= OnPaneResize;
        execution?.Cancel();
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
