using BlazorMonaco.Languages;
using Global = BlazorMonaco.Editor.Global;

namespace BlazorQL;

/// <summary>
/// The BlazorQL IDE. Renders the editor shell over BlazorMonaco editors, with every language
/// feature (completion, validation, hover, formatting) computed in C#. One instance per page.
/// </summary>
public partial class BlazorQLIde :
    IAsyncDisposable
{
    internal const string OperationElementId = "blazorql-operation-editor";
    internal const string ResponseElementId = "blazorql-response-editor";
    const string operationModelUri = "inmemory://model/blazorql-operation.graphql";
    const string variablesModelUri = "inmemory://model/blazorql-variables.json";
    const string headersModelUri = "inmemory://model/blazorql-request-headers.json";
    const string responseModelUri = "inmemory://model/blazorql-response.json";
    const string pluginResizerId = "blazorql-plugin-resizer";
    const string sessionResizerId = "blazorql-session-resizer";
    const string toolsResizerId = "blazorql-tools-resizer";
    const double collapseThreshold = 100;

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
    IGraphQLFetcher? attachedFetcher;
    JsModule? module;
    readonly BlazorQLCallbacks callbacks = new();
    DotNetObjectReference<BlazorQLCallbacks>? reference;
    CancelSource? execution;

    // Editor components and the named models they are moved onto — the models carry stable uris
    // so tests and the language providers can address each editor.
    StandaloneCodeEditor? operationEditor;
    StandaloneCodeEditor? responseEditor;
    EditorTools? editorTools;
    TextModel? operationModel;
    TextModel? variablesModel;
    TextModel? headersModel;
    TextModel? responseModel;

    // Persisted state is rehydrated before the editors render (their initial values come from the
    // active tab), so the editor components are gated on this flag.
    bool hydrated;
    bool domWired;
    int editorsInitialized;
    bool resolvedDark;

    // The one live instance the (globally registered, once) language providers route through.
    static BlazorQLIde? active;
    static bool providersRegistered;

    // Shell state. Pane ratios are the first pane's share of the container (see PaneState).
    TabStore tabs = new();
    ThemeService themes = new();
    PaneState pluginPane = new(1.0 / 3);

    PaneState sessionPane = new(0.5);

    // The operation editor's share of the editors column; 3:1 over the editor tools.
    readonly PaneState toolsPane = new(0.75);
    PluginKind? visiblePlugin;
    readonly DocExplorerNavigator docNavigator = new();
    bool toolsExpanded;
    EditorTool activeTool = EditorTool.Variables;
    bool pickerOpen;
    IReadOnlyList<OperationFact> pickerOperations = [];

    // M6 persistence: storage rides the host module's localStorage seam. Writes are debounced so
    // typing does not thrash localStorage.
    StorageService? storage;
    HistoryStore? history;
    bool persistHeaders;
    bool settingsOpen;
    bool shortKeysOpen;

    // M7: the status footer under the response pane, and the pending focus request the Ctrl-Alt-K
    // shortcut leaves for the render that opens the docs pane.
    string? statusLine;
    bool docSearchFocusPending;
    Debouncer stateDebounce = new();
    Debouncer paneDebounce = new();

    // Content-change fan-out: each editor coalesces its change bursts, and diagnostics get their
    // own window so validation lags typing rather than racing it.
    Debouncer operationChangeDebounce = new(300);
    Debouncer variablesChangeDebounce = new();
    Debouncer headersChangeDebounce = new();
    Debouncer responseChangeDebounce = new();
    Debouncer diagnosticsDebounce = new(400);

    /// <summary>The schema printed as SDL, once loaded.</summary>
    public string? SchemaSdl { get; private set; }

    /// <summary>The parsed introspection result, once loaded — what the doc explorer navigates.</summary>
    public SchemaIndex? Schema { get; private set; }

    /// <summary>Validates operations against the loaded schema. Null until introspection lands.</summary>
    SchemaValidator? validator;

    protected override async Task OnInitializedAsync()
    {
        module = new(JS);
        reference = DotNetObjectReference.Create(callbacks);
        callbacks.PaneResize += OnPaneResize;
        callbacks.GlobalShortcut += OnGlobalShortcut;
        attachedFetcher = Fetcher;

        await module.Invoke("init", reference);

        // Storage rides the freshly imported module, so everything persisted is rehydrated here —
        // before the editors take their initial values.
        storage = new(new JsStorageBackend(module), StorageNamespace);
        history = new(storage, QueryParses, MaxHistoryLength);
        Rehydrate();
        // A #q= share link wins over storage for the active tab's query and variables.
        await ApplySharedLink();

        await ApplyTheme();

        // The editors render on the next pass, seeded from the rehydrated tab state.
        hydrated = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The docs pane (and its search input) had to render before it could take focus.
        if (docSearchFocusPending && module is not null)
        {
            docSearchFocusPending = false;
            await module.Invoke("focusElement", "#blazorql-doc-search-input");
        }

        if (!hydrated || domWired)
        {
            return;
        }

        domWired = true;
        await module!.Invoke("trackPointer", pluginResizerId, "plugin", "x");
        await module.Invoke("trackPointer", sessionResizerId, "session", "x");
        await module.Invoke("trackPointer", toolsResizerId, "tools", "y");

        // Document-level shortcuts for commands that live outside any editor.
        await module.Invoke(
            "registerGlobalShortcuts",
            JsonSerializer.Serialize(
                [
                    new("refetch", "r", Ctrl: true, Shift: true, Alt: false, Meta: false),
                    new("doc-search", "k", Ctrl: true, Shift: false, Alt: true, Meta: false),
                    new("settings", ",", Ctrl: true, Shift: false, Alt: false, Meta: false)
                ],
                WebJson.Default.ShortcutArray));
    }

    /// <summary>
    /// A swapped-in fetcher instance (an endpoint change in the host app) re-attaches and
    /// re-introspects, so the schema always describes what requests will actually hit.
    /// </summary>
    protected override Task OnParametersSetAsync()
    {
        if (!hydrated ||
            ReferenceEquals(attachedFetcher, Fetcher))
        {
            return Task.CompletedTask;
        }

        attachedFetcher = Fetcher;
        // A run in flight belongs to the old fetcher.
        execution?.Cancel();
        return LoadSchema();
    }

    // ---- Editor construction and init ----

    StandaloneEditorConstructionOptions OperationOptions(StandaloneCodeEditor _) =>
        EditorDefaults.Create("graphql", "", MonacoTheme);

    StandaloneEditorConstructionOptions VariablesOptions(StandaloneCodeEditor _) =>
        EditorDefaults.Create("json", "", MonacoTheme);

    StandaloneEditorConstructionOptions HeadersOptions(StandaloneCodeEditor _) =>
        EditorDefaults.Create("json", "", MonacoTheme);

    StandaloneEditorConstructionOptions ResponseOptions(StandaloneCodeEditor _)
    {
        var options = EditorDefaults.Create("json", "", MonacoTheme);
        options.ReadOnly = true;
        options.LineNumbers = "off";
        options.WordWrap = "on";
        options.Contextmenu = false;
        return options;
    }

    string MonacoTheme =>
        resolvedDark ? "vs-dark" : "vs";

    /// <summary>
    /// Moves an editor onto a named model carrying the real initial value. The anonymous model the
    /// component created stays behind, detached and empty — BlazorMonaco's uri-keyed model lookup
    /// cannot resolve monaco's auto-generated uris, so it cannot be disposed from C#.
    /// </summary>
    async Task<TextModel> SwapModel(StandaloneCodeEditor editor, string value, string language, string uri)
    {
        var model = await NamedModels.Create(JS, value, language, uri);
        await editor.SetModel(model);
        return model;
    }

    async Task OnOperationInit()
    {
        operationModel = await SwapModel(operationEditor!, tabs.Active.Query, "graphql", operationModelUri);

        await operationEditor!.AddAction(
            new()
            {
                Id = "blazorql-run",
                Label = "Run Operation",
                ContextMenuGroupId = "graphql",
                Keybindings = [(int)KeyMod.CtrlCmd | (int)KeyCode.Enter],
                Run = _ => InvokeAsync(RunFromKeyboard)
            });
        // GraphiQL's editor bindings: Shift-Ctrl-P prettify, Shift-Ctrl-M merge, Shift-Ctrl-C copy.
        await AddPrettifyAction(operationEditor);
        await operationEditor.AddAction(
            new()
            {
                Id = "blazorql-merge",
                Label = "Merge Fragments",
                ContextMenuGroupId = "graphql",
                Keybindings = [(int)KeyMod.Shift | (int)KeyMod.WinCtrl | (int)KeyCode.KeyM],
                Run = _ => InvokeAsync(MergeFragments)
            });
        await operationEditor.AddAction(
            new()
            {
                Id = "blazorql-copy",
                Label = "Copy Query",
                ContextMenuGroupId = "graphql",
                Keybindings = [(int)KeyMod.Shift | (int)KeyMod.WinCtrl | (int)KeyCode.KeyC],
                Run = _ => InvokeAsync(CopyQuery)
            });

        await EditorReady();
    }

    async Task OnVariablesInit()
    {
        variablesModel = await SwapModel(editorTools!.VariablesEditor!, tabs.Active.Variables, "json", variablesModelUri);
        await AddPrettifyAction(editorTools.VariablesEditor!);
        await EditorReady();
    }

    async Task OnHeadersInit()
    {
        headersModel = await SwapModel(editorTools!.HeadersEditor!, tabs.Active.Headers, "json", headersModelUri);
        await AddPrettifyAction(editorTools.HeadersEditor!);
        await EditorReady();
    }

    async Task OnResponseInit()
    {
        responseModel = await SwapModel(responseEditor!, tabs.Active.Response, "json", responseModelUri);
        await EditorReady();
    }

    Task AddPrettifyAction(StandaloneCodeEditor editor) =>
        editor.AddAction(
            new()
            {
                Id = "blazorql-prettify",
                Label = "Prettify Editors",
                ContextMenuGroupId = "graphql",
                Keybindings = [(int)KeyMod.Shift | (int)KeyMod.WinCtrl | (int)KeyCode.KeyP],
                Run = _ => InvokeAsync(PrettifyEditors)
            });

    int ExpectedEditors =>
        IsHeadersEditorEnabled ? 4 : 3;

    /// <summary>Once every editor has its model, the providers register and the schema loads.</summary>
    async Task EditorReady()
    {
        editorsInitialized++;
        if (editorsInitialized != ExpectedEditors)
        {
            return;
        }

        await RegisterProviders();
        await LoadSchema();

        ready = true;
        StateHasChanged();
    }

    // ---- Language providers (registered once per page, routed through the live instance) ----

    async Task RegisterProviders()
    {
        active = this;
        if (providersRegistered)
        {
            return;
        }

        providersRegistered = true;

        var provider = new CompletionItemProvider(
            [" ", "(", "$", "@", ":", "{", "."],
            (modelUri, position, _) =>
                active?.ProvideCompletions(modelUri, position) ?? Task.FromResult(EmptyCompletions()));
        await BlazorMonaco.Languages.Global.RegisterCompletionItemProvider(JS, "graphql", provider);

        await BlazorMonaco.Languages.Global.RegisterHoverProviderAsync(
            JS,
            "graphql",
            (modelUri, position, _) =>
                // ReSharper disable once ConstantConditionalAccessQualifier
                active?.ProvideOperationHover(modelUri, position) ?? Task.FromResult<Hover>(null!));

        // Hovering a value ending in an image extension in the response editor previews the image.
        await BlazorMonaco.Languages.Global.RegisterHoverProviderAsync(
            JS,
            "json",
            (modelUri, position, _) =>
                // ReSharper disable once ConstantConditionalAccessQualifier
                active?.ProvideResponseImageHover(modelUri, position) ?? Task.FromResult<Hover>(null!));
    }

    static CompletionList EmptyCompletions() =>
        new()
        {
            Suggestions = []
        };

    async Task<CompletionList> ProvideCompletions(string modelUri, Position position)
    {
        // Monaco invokes this on every keystroke/trigger; never let an exception escape into the
        // JS interop boundary (it would surface as an unhandled Blazor error).
        try
        {
            if (Schema is null ||
                operationModel is null ||
                modelUri != operationModel.Uri)
            {
                return EmptyCompletions();
            }

            var model = await Global.GetModel(JS, modelUri);
            var text = await model.GetValue(EndOfLinePreference.LF, false);
            var offset = ToOffset(text, position.LineNumber, position.Column);
            var range = ReplacedWordRange(text, position, offset);

            return new()
            {
                Suggestions =
                [
                    .. CompletionEngine.Complete(Schema, text, offset)
                        .Select(entry => new CompletionItem
                        {
                            LabelAsString = entry.Label,
                            Kind = MapKind(entry.Kind),
                            Detail = entry.Detail,
                            DocumentationAsString = entry.Documentation,
                            SortText = entry.SortText,
                            InsertText = entry.InsertText ?? entry.Label,
                            Tags = entry.Deprecated ? [CompletionItemTag.Deprecated] : null,
                            RangeAsObject = range
                        })
                ]
            };
        }
        catch
        {
            return EmptyCompletions();
        }
    }

    async Task<Hover> ProvideOperationHover(string modelUri, Position position)
    {
        try
        {
            if (Schema is null ||
                operationModel is null ||
                modelUri != operationModel.Uri)
            {
                return null!;
            }

            var model = await Global.GetModel(JS, modelUri);
            var text = await model.GetValue(EndOfLinePreference.LF, false);
            var hover = HoverEngine.Hover(Schema, text, ToOffset(text, position.LineNumber, position.Column));
            if (hover is null)
            {
                return null!;
            }

            return new()
            {
                Contents =
                [
                    new()
                    {
                        Value = hover.Markdown
                    }
                ],
                Range = ToRange(text, hover.Start, hover.End)
            };
        }
        catch
        {
            return null!;
        }
    }

    static readonly Regex imageToken =
        new(@"\S+\.(png|svg|jpe?g|gif|webp)$", RegexOptions.IgnoreCase);

    async Task<Hover> ProvideResponseImageHover(string modelUri, Position position)
    {
        try
        {
            if (responseModel is null ||
                modelUri != responseModel.Uri)
            {
                return null!;
            }

            var model = await Global.GetModel(JS, modelUri);
            var line = await model.GetLineContent(position.LineNumber);

            var start = position.Column - 1;
            var end = start;
            while (start > 0 && !IsTokenBoundary(line[start - 1]))
            {
                start--;
            }

            while (end < line.Length && !IsTokenBoundary(line[end]))
            {
                end++;
            }

            var token = line[start..end];
            if (!imageToken.IsMatch(token))
            {
                return null!;
            }

            return new()
            {
                Contents =
                [
                    new()
                    {
                        Value = $"![]({token})"
                    }
                ],
                Range = new()
                {
                    StartLineNumber = position.LineNumber,
                    StartColumn = start + 1,
                    EndLineNumber = position.LineNumber,
                    EndColumn = end + 1
                }
            };
        }
        catch
        {
            return null!;
        }
    }

    static bool IsTokenBoundary(char ch) =>
        ch is '"' or ' ' or '\t' or ',';

    static CompletionItemKind MapKind(string kind) =>
        kind switch
        {
            "Field" => CompletionItemKind.Field,
            "Argument" => CompletionItemKind.Property,
            "EnumMember" => CompletionItemKind.EnumMember,
            "Value" => CompletionItemKind.Value,
            "Variable" => CompletionItemKind.Variable,
            "Class" => CompletionItemKind.Class,
            "Interface" => CompletionItemKind.Interface,
            "Reference" => CompletionItemKind.Reference,
            "Keyword" => CompletionItemKind.Keyword,
            _ => CompletionItemKind.Text
        };

    /// <summary>The word being completed: its start through the caret, in Monaco coordinates.</summary>
    static BlazorMonaco.Range ReplacedWordRange(string text, Position position, int offset)
    {
        var start = Math.Min(offset, text.Length);
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        return new()
        {
            StartLineNumber = position.LineNumber,
            StartColumn = position.Column - (Math.Min(offset, text.Length) - start),
            EndLineNumber = position.LineNumber,
            EndColumn = position.Column
        };
    }

    // ---- Content changes: tab state, persistence, diagnostics ----

    Task OnOperationContentChanged(ModelContentChangedEvent _)
    {
        operationChangeDebounce.Run(() =>
            InvokeAsync(async () =>
            {
                if (operationEditor is null)
                {
                    return;
                }

                // Also keeps the derived tab title live while the user types.
                tabs.Active.Query = await operationEditor.GetValue();
                SchedulePersist();
                StateHasChanged();
            }));
        ScheduleDiagnostics();
        return Task.CompletedTask;
    }

    Task OnVariablesContentChanged(ModelContentChangedEvent _)
    {
        variablesChangeDebounce.Run(() =>
            InvokeAsync(async () =>
            {
                if (editorTools?.VariablesEditor is not { } editor)
                {
                    return;
                }

                tabs.Active.Variables = await editor.GetValue();
                SchedulePersist();
                StateHasChanged();
            }));
        ScheduleDiagnostics();
        return Task.CompletedTask;
    }

    Task OnHeadersContentChanged(ModelContentChangedEvent _)
    {
        headersChangeDebounce.Run(() =>
            InvokeAsync(async () =>
            {
                if (editorTools?.HeadersEditor is not { } editor)
                {
                    return;
                }

                tabs.Active.Headers = await editor.GetValue();
                SchedulePersist();
                StateHasChanged();
            }));
        return Task.CompletedTask;
    }

    Task OnResponseContentChanged(ModelContentChangedEvent _)
    {
        responseChangeDebounce.Run(() =>
            InvokeAsync(async () =>
            {
                if (responseEditor is null)
                {
                    return;
                }

                // Tracked for tab switches, but never persisted. The response-action buttons
                // key off this value, so the change still renders.
                tabs.Active.Response = await responseEditor.GetValue();
                StateHasChanged();
            }));
        return Task.CompletedTask;
    }

    void ScheduleDiagnostics() =>
        diagnosticsDebounce.Run(() => InvokeAsync(RunDiagnostics));

    /// <summary>
    /// Validates the operation text (syntax + schema rules + deprecation warnings) into markers on
    /// the operation model, then checks the variables document against the operation's declared
    /// variables. Best-effort: diagnostics must never disrupt typing.
    /// </summary>
    async Task RunDiagnostics()
    {
        if (operationEditor is null ||
            operationModel is null)
        {
            return;
        }

        try
        {
            var text = await operationEditor.GetValue();
            var document = DocumentInfo.Parse(text);
            var markers = new List<MarkerData>();
            if (validator is not null)
            {
                foreach (var diagnostic in validator.Validate(document))
                {
                    markers.Add(ToMarker(text, diagnostic.Message, diagnostic.IsError, diagnostic.Line, diagnostic.Column));
                }
            }
            else if (document.SyntaxError is not null)
            {
                markers.Add(ToMarker(text, $"Syntax Error: {document.SyntaxError}", isError: true, document.SyntaxErrorLine, document.SyntaxErrorColumn));
            }

            await Global.SetModelMarkers(JS, operationModel, "blazorql", markers);
            await CheckVariables(document);
        }
        catch
        {
            // Diagnostics are best-effort; never disrupt typing.
        }
    }

    async Task CheckVariables(DocumentInfo document)
    {
        if (variablesModel is null ||
            editorTools?.VariablesEditor is not { } editor)
        {
            return;
        }

        var text = await editor.GetValue();
        var markers = new List<MarkerData>();
        var (ok, value, error) = Formatter.ParseJsonc(text, "Variables");
        if (!ok)
        {
            markers.Add(FirstLineMarker(error!));
        }
        else if (Schema is not null &&
                 document.OperationNode(null) is { } operation)
        {
            foreach (var message in VariablesChecker.Check(Schema, operation, value))
            {
                markers.Add(FirstLineMarker(message));
            }
        }

        await Global.SetModelMarkers(JS, variablesModel, "blazorql-variables", markers);
    }

    static MarkerData FirstLineMarker(string message) =>
        new()
        {
            Message = message,
            Severity = MarkerSeverity.Error,
            StartLineNumber = 1,
            StartColumn = 1,
            EndLineNumber = 1,
            EndColumn = 2
        };

    /// <summary>A marker spanning the word at the diagnostic's position (at least one column).</summary>
    static MarkerData ToMarker(string text, string message, bool isError, int line, int column)
    {
        line = Math.Max(line, 1);
        column = Math.Max(column, 1);
        var offset = ToOffset(text, line, column);
        var end = offset;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return new()
        {
            Message = message,
            Severity = isError ? MarkerSeverity.Error : MarkerSeverity.Warning,
            StartLineNumber = line,
            StartColumn = column,
            EndLineNumber = line,
            EndColumn = column + Math.Max(end - offset, 1)
        };
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
    static bool QueryParses(string query) =>
        DocumentInfo.Parse(query).Parses;

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
            using var cancelSource = new CancelSource(TimeSpan.FromSeconds(60));

            // An endpoint behind an Authorization header has no schema without it, so
            // introspection goes out with whatever the headers editor holds, as GraphiQL does.
            var (headers, _) = await CurrentHeaders();
            var payload = await Introspect(draftAdditions: true, headers, cancelSource.Token);
            var schema = payload is null ? null : SchemaIndex.Parse(payload.Value);

            if (schema is null)
            {
                // Everything the draft additions ask for is optional, and a server that has not
                // implemented them rejects the whole document rather than omitting the fields. So
                // one retry without them, which is the query every server can answer.
                var portable = await Introspect(draftAdditions: false, headers, cancelSource.Token);
                if (portable is not null)
                {
                    payload = portable;
                    schema = SchemaIndex.Parse(portable.Value);
                }
            }

            if (schema is null)
            {
                await SetResponse(IntrospectionFailure(payload));
                return;
            }

            Schema = schema;
            SchemaSdl = SdlPrinter.Print(schema);
            validator = new(schema);
            // Revalidate whatever is in the editors against the fresh schema.
            ScheduleDiagnostics();
            await OnSchemaLoaded.InvokeAsync();
        }
        catch (Exception exception)
        {
            await SetResponse(ErrorJson($"Introspection failed: {exception.Message}"));
        }
    }

    /// <summary>The first document the fetcher yields for an introspection request, or null.</summary>
    async Task<JsonElement?> Introspect(bool draftAdditions, IReadOnlyDictionary<string, string> headers, Cancel cancel)
    {
        await foreach (var payload in Fetcher.FetchAsync(new(IntrospectionQuery(draftAdditions)), headers, cancel))
        {
            return payload;
        }

        return null;
    }

    /// <summary>
    /// What reaches the response pane when neither attempt produced a schema. The server's own
    /// errors say far more than anything invented here, so they are what gets shown - the
    /// alternative is a generic sentence and a trip to the server logs.
    /// </summary>
    static string IntrospectionFailure(JsonElement? payload)
    {
        if (payload is not {ValueKind: JsonValueKind.Object} document)
        {
            return ErrorJson("Introspection returned no result.");
        }

        if (document.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0)
        {
            return Formatter.FormatJson(document.GetRawText());
        }

        return ErrorJson("Introspection failed: the result carries no schema.");
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

    Task CycleTheme()
    {
        themes.Cycle();
        PersistTheme();
        return ApplyTheme();
    }

    /// <summary>The settings dialog's explicit theme choice — same service as the sidebar cycle.</summary>
    Task SelectTheme(Theme theme)
    {
        themes.Current = theme;
        PersistTheme();
        return ApplyTheme();
    }

    async Task ApplyTheme()
    {
        var effective = ForcedTheme ?? themes.Current;
        var systemDark = effective == Theme.System && await module!.Invoke<bool>("systemDark");
        resolvedDark = effective == Theme.Dark || systemDark;
        await module!.Invoke("setDataTheme", resolvedDark ? "dark" : "light");
        await Global.SetTheme(JS, MonacoTheme);
    }

    // ---- Plugin pane ----

    void TogglePlugin(PluginKind plugin)
    {
        visiblePlugin = visiblePlugin == plugin
            ? null
            : plugin;
        PersistVisiblePlugin();
    }

    /// <summary>
    /// Ctrl/Cmd+click on a schema name in the operation editor jumps to its documentation. The
    /// clicked word resolves through the C# scanner against the loaded schema.
    /// </summary>
    async Task OnOperationMouseDown(EditorMouseEvent args)
    {
        var pointer = args.Event;
        var position = args.Target?.Position;
        if (Schema is null ||
            operationEditor is null ||
            pointer is null ||
            position is null ||
            pointer is { CtrlKey: false, MetaKey: false } ||
            pointer.RightButton)
        {
            return;
        }

        var text = await operationEditor.GetValue();
        var schemaReference = SchemaReferenceResolver.Resolve(Schema, text, ToOffset(text, position.LineNumber, position.Column));
        if (schemaReference is null)
        {
            return;
        }

        visiblePlugin = PluginKind.Docs;
        PersistVisiblePlugin();
        docNavigator.NavigateTo(schemaReference);
        StateHasChanged();
    }

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
                    if (pixels < collapseThreshold)
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
                    if (size - pixels < collapseThreshold)
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
        if (operationEditor is null)
        {
            return;
        }

        var tab = tabs.Active;
        tab.Query = await operationEditor.GetValue();
        if (editorTools?.VariablesEditor is { } variablesEditor)
        {
            tab.Variables = await variablesEditor.GetValue();
        }

        if (IsHeadersEditorEnabled &&
            editorTools?.HeadersEditor is { } headersEditor)
        {
            tab.Headers = await headersEditor.GetValue();
        }

        if (responseEditor is not null)
        {
            tab.Response = await responseEditor.GetValue();
        }
    }

    async Task LoadActiveTab()
    {
        if (operationEditor is null)
        {
            return;
        }

        var tab = tabs.Active;
        await operationEditor.SetValue(tab.Query);
        if (editorTools?.VariablesEditor is { } variablesEditor)
        {
            await variablesEditor.SetValue(tab.Variables);
        }

        if (IsHeadersEditorEnabled &&
            editorTools?.HeadersEditor is { } headersEditor)
        {
            await headersEditor.SetValue(tab.Headers);
        }

        if (responseEditor is not null)
        {
            await responseEditor.SetValue(tab.Response);
        }

        // The status line described the previous tab's run.
        statusLine = null;
        // Tools open themselves for a tab that has content in them, close otherwise.
        toolsExpanded =
            !string.IsNullOrWhiteSpace(tab.Variables) ||
            (IsHeadersEditorEnabled && !string.IsNullOrWhiteSpace(tab.Headers));
    }

    // ---- Execution ----

    /// <summary>Document-level shortcuts registered with the host module.</summary>
    void OnGlobalShortcut(string id) =>
        _ = InvokeAsync(() =>
        {
            switch (id)
            {
                case "refetch":
                    if (!refetching)
                    {
                        return RefetchSchema();
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

            return Task.CompletedTask;
        });

    // ---- Toolbar operations ----

    /// <summary>Prettifies every editor, in GraphiQL's order: variables, headers, then the query.</summary>
    async Task PrettifyEditors()
    {
        await PrettifyJson(editorTools?.VariablesEditor);
        if (IsHeadersEditorEnabled)
        {
            await PrettifyJson(editorTools?.HeadersEditor);
        }

        if (operationEditor is null)
        {
            return;
        }

        var text = await operationEditor.GetValue();
        if (text.Trim().Length == 0)
        {
            return;
        }

        var formatted = Formatter.FormatGraphQL(text);
        if (formatted != text)
        {
            await operationEditor.SetValue(formatted);
        }
    }

    static async Task PrettifyJson(StandaloneCodeEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        var text = await editor.GetValue();
        if (text.Trim().Length == 0)
        {
            return;
        }

        var formatted = Formatter.FormatJson(text);
        if (formatted != text)
        {
            await editor.SetValue(formatted);
        }
    }

    /// <summary>Inlines named fragments into the operations. A parse failure becomes the response.</summary>
    async Task MergeFragments()
    {
        if (operationEditor is null)
        {
            return;
        }

        var text = await operationEditor.GetValue();
        var (ok, merged, error) = FragmentMerger.Merge(text);
        if (!ok)
        {
            await SetResponse(ErrorJson(error ?? "Merge failed."));
            return;
        }

        if (merged != text)
        {
            await operationEditor.SetValue(merged!);
        }
    }

    async Task CopyQuery()
    {
        var query = await operationEditor!.GetValue();
        await module!.Invoke("copyText", query);
    }

    /// <summary>Writes the query + variables into the location hash and copies the resulting link.</summary>
    async Task ShareQuery()
    {
        var variables = editorTools?.VariablesEditor is { } variablesEditor
            ? await variablesEditor.GetValue()
            : "";
        var shared = new SharedQuery(await operationEditor!.GetValue(), variables);
        var href = await module!.Invoke<string>("setHash", ShareLinkCodec.Encode(shared));
        await module.Invoke("copyText", href);
    }

    async Task CopyResponse()
    {
        var response = await responseEditor!.GetValue();
        await module!.Invoke("copyText", response);
    }

    async Task DownloadResponse()
    {
        var response = await responseEditor!.GetValue();
        await module!.Invoke("downloadText", "response.json", response, "application/json");
    }

    /// <summary>Ctrl-Enter: with several operations in the document the caret decides.</summary>
    async Task RunFromKeyboard()
    {
        if (running)
        {
            execution?.Cancel();
            return;
        }

        var query = await operationEditor!.GetValue();
        var operations = DocumentInfo.Parse(query).Operations;
        string? operationName = null;
        if (operations.Count > 1)
        {
            var position = await operationEditor.GetPosition();
            var offset = position is null ? 0 : ToOffset(query, position.LineNumber, position.Column);
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

        var query = await operationEditor!.GetValue();
        var operations = DocumentInfo.Parse(query).Operations;
        if (operations.Count > 1)
        {
            pickerOperations = operations;
            pickerOpen = true;
            return;
        }

        await Run(query, operations.Count == 1 ? operations[0].Name : null, multipleOperations: false);
    }

    async Task RunPicked(OperationFact operation)
    {
        pickerOpen = false;
        var query = await operationEditor!.GetValue();
        await Run(query, operation.Name, multipleOperations: true);
    }

    async Task Run(string query, string? operationName, bool multipleOperations)
    {
        // Fill in default leaf selections first; the filled text is what runs (and what the user
        // sees, briefly highlighted).
        query = await FillLeafs(query);

        // The parse errors short-circuit: nothing is sent, the error is the response.
        var variables = await ParseEditorJson(editorTools?.VariablesEditor, "Variables");
        if (variables.Error is not null)
        {
            await SetResponse(ErrorJson(variables.Error));
            return;
        }

        var (headers, headersError) = await CurrentHeaders();
        if (headersError is not null)
        {
            await SetResponse(ErrorJson(headersError));
            return;
        }

        // The operation actually run names the tab (only meaningful when the caret or picker had
        // to disambiguate).
        if (multipleOperations)
        {
            tabs.Active.OperationName = operationName;
            SchedulePersist();
        }

        // The history records every execution start, whether or not its pane is open.
        var variablesText = editorTools?.VariablesEditor is { } variablesEditor
            ? await variablesEditor.GetValue()
            : "";
        var headersText = IsHeadersEditorEnabled && editorTools?.HeadersEditor is { } headersEditor
            ? await headersEditor.GetValue()
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
        // Elapsed covers the full fetch; the status text is what the footer line shows. An HTTP
        // fetcher contributes its status code; elsewhere "OK" stands in for success.
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
            await SetResponse(ErrorJson(exception.Message));
        }
        finally
        {
            stopwatch.Stop();
            // The sidecar decorator is transparent for the footer — look through it at the transport.
            var transport = Fetcher is SidecarFetcher sidecar
                ? sidecar.Inner
                : Fetcher;
            // The HTTP status code replaces "OK" outright; error/stopped wording still wins a slot.
            statusLine = transport is HttpFetcher { LastStatus: { } httpStatus }
                ? status == "OK"
                    ? $"{httpStatus.StatusCode} · {stopwatch.ElapsedMilliseconds} ms"
                    : $"{httpStatus.StatusCode} · {status} · {stopwatch.ElapsedMilliseconds} ms"
                : $"{status} · {stopwatch.ElapsedMilliseconds} ms";
            execution.Dispose();
            execution = null;
            running = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Fills in default leaf selections for fields that need them, returning the text the run
    /// sends. Inserted ranges are highlighted for a few seconds so the user sees what was added.
    /// The cursor keeps its position rather than being remapped through the insertion offsets.
    /// </summary>
    async Task<string> FillLeafs(string query)
    {
        if (Schema is null ||
            operationEditor is null)
        {
            return query;
        }

        try
        {
            var (result, insertions) = LeafFiller.Fill(Schema, query);
            if (insertions.Count == 0)
            {
                return query;
            }

            var position = await operationEditor.GetPosition();
            await operationEditor.SetValue(result);
            if (position is not null)
            {
                await operationEditor.SetPosition(position, "blazorql");
            }

            await DecorateInsertions(result, insertions);
            return result;
        }
        catch
        {
            return query;
        }
    }

    async Task DecorateInsertions(string text, IReadOnlyList<LeafFiller.Insertion> insertions)
    {
        // Insertion indices address the original text; earlier insertions shift the later ones.
        var shift = 0;
        var decorations = new List<ModelDeltaDecoration>();
        foreach (var insertion in insertions.OrderBy(_ => _.Index))
        {
            var start = insertion.Index + shift;
            decorations.Add(new()
            {
                Range = ToRange(text, start, start + insertion.Text.Length),
                Options = new()
                {
                    ClassName = "blazorql-auto-inserted-leaf",
                    HoverMessage =
                    [
                        new()
                        {
                            Value = "Automatically added leaf fields"
                        }
                    ]
                }
            });
            shift += insertion.Text.Length;
        }

        var ids = await operationEditor!.DeltaDecorations(null, [.. decorations]);
        _ = ClearDecorations(ids);
    }

    async Task ClearDecorations(string[] ids)
    {
        await Task.Delay(TimeSpan.FromSeconds(7));
        try
        {
            await operationEditor!.DeltaDecorations(ids, []);
        }
        catch
        {
            // The editor may be gone; the highlight going with it is fine.
        }
    }

    /// <summary>Runs the given editor's content through the shared JSONC parser.</summary>
    static async Task<(JsonElement? Value, string? Error)> ParseEditorJson(StandaloneCodeEditor? editor, string what)
    {
        var text = editor is null
            ? ""
            : await editor.GetValue();
        var (ok, value, error) = Formatter.ParseJsonc(text, what);
        return ok
            ? (value, null)
            : (null, error);
    }

    /// <summary>One synthesized error, rendered into the response pane like any other result.</summary>
    static string ErrorJson(string message) =>
        JsonSerializer.Serialize(ErrorDocument.From(message), WebJson.Default.ErrorDocument);

    /// <summary>
    /// The headers for an outgoing request. The headers editor is the live source once it exists;
    /// before that, and whenever the tool is turned off, the tab carries them -- which is where
    /// <see cref="DefaultHeaders"/> and a restored session land. The error is for a caller that has
    /// somewhere to show it; introspection sends what parsed and lets the request fail on its own.
    /// </summary>
    async Task<(Dictionary<string, string> Headers, string? Error)> CurrentHeaders()
    {
        if (IsHeadersEditorEnabled &&
            editorTools?.HeadersEditor is {} editor)
        {
            var (value, error) = await ParseEditorJson(editor, "Request headers");
            return error is null
                ? (ToHeaderDictionary(value), null)
                : (emptyHeaders, error);
        }

        var (ok, parsed, tabError) = Formatter.ParseJsonc(tabs.Active.Headers, "Request headers");
        return ok
            ? (ToHeaderDictionary(parsed), null)
            : (emptyHeaders, tabError);
    }

    static Dictionary<string, string> ToHeaderDictionary(JsonElement? parsed)
    {
        Dictionary<string, string> headers = [];
        if (parsed is not { ValueKind: JsonValueKind.Object } element)
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

    async ValueTask SetResponse(string text)
    {
        if (responseEditor is not null)
        {
            await responseEditor.SetValue(text);
        }
    }

    /// <summary>Whether the response pane shows anything — gates the copy/download overlay.</summary>
    bool HasResponse =>
        ready && !string.IsNullOrWhiteSpace(tabs.Active.Response);

    /// <summary>
    /// The errors the current response carried. Recomputed from the response text rather than
    /// cached alongside it, so a response arriving by any route — executed, merged from an
    /// incremental payload, restored from storage — is covered by the same code.
    /// </summary>
    IReadOnlyList<ResponseError> ResponseFieldErrors =>
        ready ? ResponseErrors.Parse(tabs.Active.Response) : [];

    /// <summary>
    /// Takes the field an error points at out of the operation. Useful after a broad exploratory
    /// query where a few fields failed and the rest returned: this gets to the subset that works
    /// without hand-editing, which is the whole reason a generated query is worth running.
    /// </summary>
    /// <remarks>
    /// Deliberately per error rather than one button that strips them all. Removal is not always
    /// the right answer — a field that failed for want of an argument wants the argument — so the
    /// choice stays with the reader, one field at a time.
    /// </remarks>
    async Task RemoveErroredField(ResponseError error)
    {
        if (operationEditor is null)
        {
            return;
        }

        var text = await operationEditor.GetValue();
        var removed = FieldRemover.Remove(text, error.Path);
        if (removed is null)
        {
            // The path does not resolve against what the editor holds now, or the field is all the
            // operation selects. Either way there is no edit to make that leaves a valid document.
            statusLine = $"Could not remove {error.PathText} from the operation.";
            return;
        }

        await operationEditor.SetValue(removed);
        statusLine = $"Removed {error.PathText} from the operation.";
    }

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

        await operationEditor!.SetValue(tab.Query);
        if (editorTools?.VariablesEditor is { } variablesEditor)
        {
            await variablesEditor.SetValue(tab.Variables);
        }

        if (IsHeadersEditorEnabled &&
            editorTools?.HeadersEditor is { } headersEditor)
        {
            await headersEditor.SetValue(tab.Headers);
        }

        SchedulePersist();
    }

    /// <summary>
    /// A document generated from the documentation explorer goes into a new tab, unless the active
    /// tab is blank — then it takes the blank tab rather than leaving it behind.
    /// </summary>
    /// <summary>
    /// Puts a generated document on the clipboard rather than in the editor. The route a fragment
    /// takes, since a document of one fragment has no operation to run - and a shortcut for a query
    /// that is wanted somewhere other than this tab.
    /// </summary>
    async Task CopyGenerated(string generated)
    {
        await module!.Invoke("copyText", generated);
        statusLine = "Copied to the clipboard.";
    }

    async Task LoadGeneratedQuery(string query)
    {
        if (operationEditor is null)
        {
            return;
        }

        execution?.Cancel();
        pickerOpen = false;
        await SaveActiveTab();
        if (!string.IsNullOrWhiteSpace(tabs.Active.Query))
        {
            tabs.Add("", DefaultHeaders ?? "");
        }

        tabs.Active.Query = query;
        await LoadActiveTab();
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

    static readonly Dictionary<string, string> emptyHeaders = [];

    // ---- Coordinate helpers (Monaco is 1-based line/column; the language services use offsets) ----

    static int ToOffset(string text, int line, int column)
    {
        var offset = 0;
        var currentLine = 1;
        while (currentLine < line && offset < text.Length)
        {
            if (text[offset] == '\n')
            {
                currentLine++;
            }

            offset++;
        }

        return Math.Min(offset + (column - 1), text.Length);
    }

    static BlazorMonaco.Range ToRange(string text, int start, int end)
    {
        var (startLine, startColumn) = ToLineColumn(text, start);
        var (endLine, endColumn) = ToLineColumn(text, end);
        return new()
        {
            StartLineNumber = startLine,
            StartColumn = startColumn,
            EndLineNumber = endLine,
            EndColumn = endColumn
        };
    }

    static (int Line, int Column) ToLineColumn(string text, int offset)
    {
        var line = 1;
        var column = 1;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    public async ValueTask DisposeAsync()
    {
        callbacks.PaneResize -= OnPaneResize;
        callbacks.GlobalShortcut -= OnGlobalShortcut;
        execution?.Cancel();
        stateDebounce.Dispose();
        paneDebounce.Dispose();
        operationChangeDebounce.Dispose();
        variablesChangeDebounce.Dispose();
        headersChangeDebounce.Dispose();
        responseChangeDebounce.Dispose();
        diagnosticsDebounce.Dispose();
        if (ReferenceEquals(active, this))
        {
            active = null;
        }

        reference?.Dispose();
        // The editors can close and reopen — a route change is enough. The models must go with
        // them or the next instance would collide with the leaked models' uris.
        await NamedModels.Dispose(operationModel);
        await NamedModels.Dispose(variablesModel);
        await NamedModels.Dispose(headersModel);
        await NamedModels.Dispose(responseModel);
        if (module is not null)
        {
            await module.DisposeAsync();
        }
    }

    /// <summary>
    /// The standard introspection query, as graphql-js emits it: descriptions, deprecated members,
    /// and nine levels of type nesting.
    /// </summary>
    /// <param name="draftAdditions">
    /// Whether to ask for the four members later spec drafts added - <c>__Schema.description</c>,
    /// <c>__Type.specifiedByURL</c>, <c>__Directive.isRepeatable</c>, and deprecation on input
    /// values, which is the isDeprecated/deprecationReason pair plus the <c>includeDeprecated</c>
    /// arguments that reach them.
    /// </param>
    /// <remarks>
    /// graphql-js leaves every one of these off by default, because a server is free not to
    /// implement them and one that has not rejects the entire document rather than omitting a
    /// field - GraphQL.NET, for one, gates them behind schema features that default to off. They
    /// are worth asking for, because they are what the doc explorer shows; they are not worth
    /// failing over, hence the retry in <see cref="LoadSchema"/>.
    /// </remarks>
    internal static string IntrospectionQuery(bool draftAdditions)
    {
        var schemaDescription = draftAdditions ? "description" : "";
        var specifiedBy = draftAdditions ? "specifiedByURL" : "";
        var isRepeatable = draftAdditions ? "isRepeatable" : "";
        // fields() and enumValues() have carried this argument since long before the drafts; only
        // args() and inputFields() are part of the input-value deprecation addition.
        var deprecatedInputs = draftAdditions ? "(includeDeprecated: true)" : "";
        var inputDeprecation = draftAdditions ? "isDeprecated deprecationReason" : "";

        return $$"""
            query IntrospectionQuery {
              __schema {
                {{schemaDescription}}
                queryType { name kind }
                mutationType { name kind }
                subscriptionType { name kind }
                types { ...FullType }
                directives {
                  name
                  description
                  {{isRepeatable}}
                  locations
                  args{{deprecatedInputs}} { ...InputValue }
                }
              }
            }

            fragment FullType on __Type {
              kind
              name
              description
              {{specifiedBy}}
              fields(includeDeprecated: true) {
                name
                description
                args{{deprecatedInputs}} { ...InputValue }
                type { ...TypeRef }
                isDeprecated
                deprecationReason
              }
              inputFields{{deprecatedInputs}} { ...InputValue }
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
              {{inputDeprecation}}
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
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
    }

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
