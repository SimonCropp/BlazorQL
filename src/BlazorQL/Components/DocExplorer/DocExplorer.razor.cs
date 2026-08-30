using System.Text;
using BlazorMonaco.Editor;

namespace BlazorQL;

/// <summary>
/// The documentation explorer: GraphiQL-style stack navigation over the introspected schema, a
/// debounced search, and a read-only SDL view backed by a lazily created BlazorMonaco editor.
/// </summary>
public partial class DocExplorer :
    IAsyncDisposable
{
    internal const string SdlElementId = "blazorql-doc-sdl-editor";
    const string SdlModelUri = "inmemory://model/blazorql-schema.graphql";
    const int SearchDebounceMs = 200;
    const int SearchResultCap = 100;

    [Inject]
    public IJSRuntime JS { get; set; } = null!;

    /// <summary>The parsed schema. Null renders the no-schema placeholder.</summary>
    [Parameter]
    public SchemaIndex? Schema { get; set; }

    /// <summary>The schema printed as SDL — the SDL view's content.</summary>
    [Parameter]
    public string? SchemaSdl { get; set; }

    /// <summary>Receives jump-to-doc navigation from the IDE.</summary>
    [Parameter]
    public DocExplorerNavigator? Navigator { get; set; }

    // The navigation stack always holds at least the root entry.
    readonly List<DocEntry> stack = [new DocRootEntry()];
    SchemaIndex? lastSchema;
    string? lastSdl;

    // Search state. Null result lists mean the dropdown is closed.
    string searchTerm = "";
    List<SearchMatch>? withinResults;
    List<SearchMatch>? otherResults;
    CancellationTokenSource? searchDebounce;

    // SDL view state. The editor is created once, on the first toggle, and then only shown/hidden.
    bool sdlVisible;
    bool sdlCreated;
    StandaloneCodeEditor? sdlEditor;
    TextModel? sdlModel;

    DocEntry Current => stack[^1];
    DocEntry Previous => stack[^2];

    bool ShowSearch =>
        Current is DocRootEntry ||
        (Current is DocTypeEntry entry && entry.Type.Kind is "OBJECT" or "INTERFACE" or "INPUT_OBJECT");

    string SearchPlaceholder =>
        Current is DocTypeEntry entry
            ? $"Search {entry.Type.Name}..."
            : "Search schema...";

    protected override void OnInitialized()
    {
        // The first schema is not a "reload" — without this, OnParametersSetAsync would reset the
        // stack right after a pending jump-to-doc navigation is applied below.
        lastSchema = Schema;
        if (Navigator is null)
        {
            return;
        }

        Navigator.Navigated += OnNavigated;
        var pending = Navigator.TakePending();
        if (pending is not null)
        {
            Apply(pending);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // A reloaded schema resets the stack to the root; rebuilding the old stack against the new
        // schema is not attempted.
        if (!ReferenceEquals(Schema, lastSchema))
        {
            lastSchema = Schema;
            stack.RemoveRange(1, stack.Count - 1);
            CloseSearch();
        }

        if (sdlModel is not null && SchemaSdl != lastSdl)
        {
            lastSdl = SchemaSdl;
            await sdlModel.SetValue(SchemaSdl ?? "");
        }
    }

    // ---- Navigation ----

    void Push(DocEntry entry)
    {
        if (!entry.SameAs(Current))
        {
            stack.Add(entry);
        }

        CloseSearch();
        sdlVisible = false;
    }

    void Pop()
    {
        if (stack.Count > 1)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        CloseSearch();
    }

    void NavigateToTypeName(string name)
    {
        var type = Schema?.Find(name);
        if (type is not null)
        {
            Push(new DocTypeEntry(type));
        }
    }

    void NavigateToField(IntrospectionType parent, string fieldName) =>
        Push(new DocFieldEntry(parent, fieldName));

    void OnNavigated(SchemaReference reference) =>
        _ = InvokeAsync(() =>
        {
            Apply(reference);
            StateHasChanged();
        });

    void Apply(SchemaReference reference)
    {
        var type = Schema?.Find(reference.TypeName);
        if (type is null)
        {
            return;
        }

        Push(new DocTypeEntry(type));
        if (reference.Kind is "Field" or "Argument" && reference.FieldName is not null)
        {
            Push(new DocFieldEntry(type, reference.FieldName));
        }
    }

    // ---- Search ----

    void OnSearchInput(ChangeEventArgs args)
    {
        searchTerm = args.Value?.ToString() ?? "";
        searchDebounce?.Cancel();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            withinResults = null;
            otherResults = null;
            return;
        }

        searchDebounce = new();
        _ = DebounceSearch(searchTerm, searchDebounce.Token);
    }

    async Task DebounceSearch(string term, Cancel cancel)
    {
        try
        {
            await Task.Delay(SearchDebounceMs, cancel);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            ComputeSearch(term);
            StateHasChanged();
        });
    }

    void ComputeSearch(string term)
    {
        List<SearchMatch> within = [];
        List<SearchMatch> other = [];
        if (Schema is null)
        {
            withinResults = within;
            otherResults = other;
            return;
        }

        var currentTypeName = (Current as DocTypeEntry)?.Type.Name;
        var total = 0;
        foreach (var type in Schema.Types)
        {
            if (type.Name.StartsWith("__", StringComparison.Ordinal))
            {
                continue;
            }

            // Matches rooted in the currently open type render first, as the implicit
            // "within" group.
            var bucket = type.Name == currentTypeName
                ? within
                : other;

            if (type.Name != currentTypeName &&
                Matches(type.Name, term) &&
                Add(other, new(type), ref total))
            {
                return;
            }

            foreach (var field in type.Fields ?? [])
            {
                if (Matches(field.Name, term) &&
                    Add(bucket, new(type, field.Name, field.Type), ref total))
                {
                    return;
                }

                foreach (var argument in field.Args)
                {
                    if (Matches(argument.Name, term) &&
                        Add(bucket, new(type, field.Name, field.Type, argument.Name, argument.Type), ref total))
                    {
                        return;
                    }
                }
            }

            foreach (var inputField in type.InputFields ?? [])
            {
                if (Matches(inputField.Name, term) &&
                    Add(bucket, new(type, inputField.Name, inputField.Type), ref total))
                {
                    return;
                }
            }
        }

        withinResults = within;
        otherResults = other;

        bool Add(List<SearchMatch> bucket, SearchMatch match, ref int count)
        {
            bucket.Add(match);
            if (++count < SearchResultCap)
            {
                return false;
            }

            withinResults = within;
            otherResults = other;
            return true;
        }
    }

    static bool Matches(string name, string term) =>
        name.Contains(term, StringComparison.OrdinalIgnoreCase);

    static string MatchText(SearchMatch match)
    {
        if (match.FieldName is null)
        {
            return match.Type.Name;
        }

        var builder = new StringBuilder($"{match.Type.Name}.{match.FieldName}");
        if (match.ArgumentName is not null)
        {
            builder.Append($"({match.ArgumentName}: {match.ArgumentType?.Display()})");
        }

        return builder.ToString();
    }

    void SelectMatch(SearchMatch match)
    {
        if (match.FieldName is null)
        {
            Push(new DocTypeEntry(match.Type));
            return;
        }

        // Field and argument matches both land on the field's page; the parent type page goes
        // onto the stack first so back walks up naturally.
        Push(new DocTypeEntry(match.Type));
        Push(new DocFieldEntry(match.Type, match.FieldName));
    }

    void CloseSearch()
    {
        searchDebounce?.Cancel();
        searchTerm = "";
        withinResults = null;
        otherResults = null;
    }

    // ---- SDL view ----

    StandaloneEditorConstructionOptions SdlOptions(StandaloneCodeEditor _)
    {
        // A null theme keeps whatever theme the IDE has set globally.
        var options = EditorDefaults.Create("graphql", SchemaSdl ?? "", theme: null);
        options.ReadOnly = true;
        options.WordWrap = "on";
        options.Contextmenu = false;
        return options;
    }

    /// <summary>
    /// Moves the editor onto a named model so the SDL view is addressable by uri (tests, and the
    /// value push in <see cref="OnParametersSetAsync"/>). The anonymous model the component
    /// created stays behind, detached and empty — BlazorMonaco's uri-keyed model lookup cannot
    /// resolve monaco's auto-generated uris, so it cannot be disposed from C#.
    /// </summary>
    async Task OnSdlEditorInit()
    {
        if (sdlEditor is null)
        {
            return;
        }

        lastSdl = SchemaSdl;
        sdlModel = await Global.CreateModel(JS, SchemaSdl ?? "", "graphql", SdlModelUri);
        await sdlEditor.SetModel(sdlModel);
    }

    void ToggleSdl()
    {
        if (SchemaSdl is null)
        {
            return;
        }

        sdlVisible = !sdlVisible;
        if (sdlVisible)
        {
            // Created once, on the first toggle; after that the editor is only shown/hidden.
            sdlCreated = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Navigator?.Navigated -= OnNavigated;
        searchDebounce?.Cancel();
        if (sdlModel is null)
        {
            return;
        }

        // The pane can close and reopen; the model must go with the component or the next
        // creation would collide with the leaked model's uri.
        try
        {
            await sdlModel.DisposeModel();
        }
        catch (JSException)
        {
            // Best-effort cleanup; the editor may already be gone.
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the editor with it.
        }
    }
}
