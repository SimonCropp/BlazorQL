namespace BlazorQL;

/// <summary>
/// The ordered tabs and which one is active. Pure state — the component moves editor content in
/// and out of the active tab around activation changes.
/// </summary>
public sealed partial class TabStore
{
    readonly List<TabState> tabs = [];

    public IReadOnlyList<TabState> Tabs => tabs;

    public int ActiveIndex { get; private set; }

    public TabState Active => tabs[ActiveIndex];

    /// <summary>Appends a tab and makes it active.</summary>
    public TabState Add(string query = "", string headers = "")
    {
        var tab = new TabState
        {
            Query = query,
            Headers = headers
        };
        tabs.Add(tab);
        ActiveIndex = tabs.Count - 1;
        return tab;
    }

    public void Activate(int index) =>
        ActiveIndex = index;

    /// <summary>
    /// Removes the tab, keeping the active tab sensible: closing the active tab activates its
    /// neighbour; closing an earlier tab shifts the active index down with the list. False when the
    /// tab is the last one, which is refused — an empty store has no <see cref="Active"/> to give,
    /// and everything reads it. The tab bar hides the close button in that case, but the rule
    /// belongs here rather than in the markup.
    /// </summary>
    public bool Close(int index)
    {
        if (tabs.Count <= 1)
        {
            return false;
        }

        tabs.RemoveAt(index);
        if (ActiveIndex >= tabs.Count)
        {
            ActiveIndex = tabs.Count - 1;
        }
        else if (index < ActiveIndex)
        {
            ActiveIndex--;
        }

        return true;
    }

    // What one tab looks like on disk — GraphiQL's tabState shape. Response is deliberately
    // absent: results are never persisted.
    sealed record PersistedTab(
        Guid Id,
        // ReSharper disable once NotAccessedPositionalProperty.Local
        string? Title,
        string? Query,
        string? Variables,
        string? Headers,
        string? OperationName,
        string? RenameOverride);

    sealed record PersistedState(int ActiveTabIndex, List<PersistedTab?>? Tabs);

    /// <summary>
    /// Nested so the persisted shapes can stay private: what one tab looks like on disk is not part
    /// of this type's surface, and a sibling context could not see them.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(PersistedState))]
    partial class TabJson :
        JsonSerializerContext;

    /// <summary>
    /// The store as its persisted JSON. Headers travel only when <paramref name="includeHeaders"/>
    /// (the persist-headers setting) allows; responses never do.
    /// </summary>
    public string Serialize(bool includeHeaders)
    {
        var state = new PersistedState(
            ActiveIndex,
            [
                .. tabs.Select(_ => new PersistedTab(
                    _.Id,
                    Title(_),
                    _.Query,
                    _.Variables,
                    includeHeaders ? _.Headers : null,
                    _.OperationName,
                    _.RenameOverride))
            ]);
        return JsonSerializer.Serialize(state, TabJson.Default.PersistedState);
    }

    /// <summary>
    /// Replaces the store's content from persisted JSON. False (invalid, absent, or empty state)
    /// leaves the store untouched so the caller can seed the default tab. The restored active
    /// index is clamped into range.
    /// </summary>
    public bool TryRestore(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        PersistedState? state;
        try
        {
            state = JsonSerializer.Deserialize(json, TabJson.Default.PersistedState);
        }
        catch (JsonException)
        {
            return false;
        }

        if (state?.Tabs is null ||
            state.Tabs.Count == 0 ||
            state.Tabs.Any(_ => _ is null))
        {
            return false;
        }

        tabs.Clear();
        foreach (var persisted in state.Tabs.OfType<PersistedTab>())
        {
            tabs.Add(new()
            {
                Id = persisted.Id == Guid.Empty
                    ? Guid.NewGuid()
                    : persisted.Id,
                Query = persisted.Query ?? "",
                Variables = persisted.Variables ?? "",
                Headers = persisted.Headers ?? "",
                OperationName = persisted.OperationName,
                RenameOverride = persisted.RenameOverride
            });
        }

        ActiveIndex = Math.Clamp(state.ActiveTabIndex, 0, tabs.Count - 1);
        return true;
    }

    // GraphiQL's fuzzy operation-name extraction: the first non-comment line that declares a named
    // operation. Comment lines are skipped by the (?!#) guard.
    //
    // Source-generated rather than constructed. A WebAssembly build runs the regex interpreter --
    // RegexOptions.Compiled needs codegen that is not there once the app is AOT-compiled, and would
    // be dropped by trimming anyway -- and the interpreter is where this pattern is slowest, on the
    // document that never matches: .* takes the whole line and then gives it back a character at a
    // time looking for the keyword, once per line. That is the shape of the anonymous shorthand a
    // fresh tab starts with, and generating the matcher at build time is 15x faster on it.
    //
    // The accessibility modifier is not a style choice here: a partial method returning a value has
    // to carry one.
    [GeneratedRegex(@"^(?!#).*(query|subscription|mutation)\s+([a-zA-Z0-9_]+)", RegexOptions.Multiline)]
    private static partial Regex OperationName();

    /// <summary>The tab's display title: an explicit rename wins, then the operation last executed,
    /// then a name fuzzily extracted from the document, then a placeholder.</summary>
    public static string Title(TabState tab)
    {
        if (!string.IsNullOrWhiteSpace(tab.RenameOverride))
        {
            return tab.RenameOverride;
        }

        if (!string.IsNullOrWhiteSpace(tab.OperationName))
        {
            return tab.OperationName;
        }

        var match = OperationName().Match(tab.Query);
        if (match.Success)
        {
            return match.Groups[2].Value;
        }

        return "<untitled>";
    }
}
