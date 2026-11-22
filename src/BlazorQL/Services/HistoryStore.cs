namespace BlazorQL;

/// <summary>
/// The execution history, mirroring GraphiQL's HistoryStore: capped LRU for ordinary items,
/// unlimited favorites kept apart, both persisted newest-first.
/// </summary>
public sealed partial class HistoryStore
{
    // Queries longer than this are noise (a pasted schema, generated documents) and are not
    // worth a history slot — GraphiQL's MAX_QUERY_SIZE.
    const int maxQueryLength = 100_000;

    StorageService storage;
    Func<string, bool> queryParses;
    int maxLength;
    List<HistoryItem> items = [];
    List<HistoryItem> favorites = [];

    sealed record PersistedQueries(List<HistoryItem?>? Queries);

    sealed record PersistedFavorites(List<HistoryItem?>? Favorites);

    /// <summary>
    /// Nested so the persisted shapes can stay private: what one history entry looks like on disk
    /// is not part of this type's surface, and a sibling context could not see them.
    /// </summary>
    [JsonSourceGenerationOptions(
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(PersistedQueries))]
    [JsonSerializable(typeof(PersistedFavorites))]
    partial class HistoryJson :
        JsonSerializerContext;

    /// <param name="storage">The namespaced store the two history keys live in.</param>
    /// <param name="queryParses">Whether a query text parses as GraphQL — injected so tests can
    /// stub the host module's getOperationFacts.</param>
    /// <param name="maxLength">The non-favorite cap; favorites are never evicted.</param>
    public HistoryStore(StorageService storage, Func<string, bool> queryParses, int maxLength = 20)
    {
        this.storage = storage;
        this.queryParses = queryParses;
        this.maxLength = maxLength;
        Load();
    }

    /// <summary>Ordinary (non-favorite) items, newest first.</summary>
    public IReadOnlyList<HistoryItem> Items => items;

    /// <summary>Favorite items, newest first.</summary>
    public IReadOnlyList<HistoryItem> Favorites => favorites;

    void Load()
    {
        items.AddRange(Parse(storage.Get("queries"), static _ =>
            JsonSerializer.Deserialize(_, HistoryJson.Default.PersistedQueries)?.Queries));
        favorites.AddRange(Parse(storage.Get("favorites"), static _ =>
            JsonSerializer.Deserialize(_, HistoryJson.Default.PersistedFavorites)?.Favorites));
        foreach (var favorite in favorites)
        {
            favorite.Favorite = true;
        }
    }

    static IEnumerable<HistoryItem> Parse(string? json, Func<string, List<HistoryItem?>?> deserialize)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return deserialize(json)?.OfType<HistoryItem>() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Writes both lists. When the store refuses the ordinary items for space, the oldest of them go
    /// until the rest fits, so what is stored is always what a reload would show rather than
    /// whatever last managed to fit. Favorites are never evicted; they are the user's choice.
    /// </summary>
    void Save()
    {
        storage.Set("favorites", JsonSerializer.Serialize(new([.. favorites]), HistoryJson.Default.PersistedFavorites));
        // Serialized again after each eviction, since what is written has to be what is left.
        while (!storage.Set("queries", SerializeItems()) &&
               items.Count > 0)
        {
            items.RemoveAt(items.Count - 1);
        }
    }

    string SerializeItems() =>
        JsonSerializer.Serialize(new([.. items]), HistoryJson.Default.PersistedQueries);

    /// <summary>
    /// Evicts the oldest ordinary item, for a write that matters more (the tab state) and has been
    /// refused for space. False when there is none left to evict.
    /// </summary>
    public bool MakeRoom()
    {
        if (items.Count == 0)
        {
            return false;
        }

        items.RemoveAt(items.Count - 1);
        storage.Set("queries", SerializeItems());
        return true;
    }

    /// <summary>
    /// Records an execution. Skipped outright for an empty, unparseable, or oversized query, and
    /// for an exact repeat (query+variables+headers) of the most recent item.
    /// </summary>
    public void Record(string query, string? variables, string? headers, string? operationName)
    {
        // Variables and headers are held to the same limit as the query: a pasted file's worth of
        // either is noise too, and nothing else stops a few of them filling the storage quota.
        if (string.IsNullOrWhiteSpace(query) ||
            query.Length > maxQueryLength ||
            variables?.Length > maxQueryLength ||
            headers?.Length > maxQueryLength ||
            !queryParses(query))
        {
            return;
        }

        var head = items.FirstOrDefault();
        if (head is not null &&
            head.Query == query &&
            head.Variables == variables &&
            head.Headers == headers)
        {
            return;
        }

        items.Insert(0, new()
        {
            Query = query,
            Variables = variables,
            Headers = headers,
            OperationName = operationName
        });

        // LRU: only ordinary items are capped; favorites live in their own unlimited list.
        while (items.Count > maxLength)
        {
            items.RemoveAt(items.Count - 1);
        }

        Save();
    }

    /// <summary>Moves the item between the ordinary and favorite lists.</summary>
    public void ToggleFavorite(HistoryItem item)
    {
        if (item.Favorite)
        {
            item.Favorite = false;
            favorites.Remove(item);
            items.Insert(0, item);
        }
        else
        {
            item.Favorite = true;
            items.Remove(item);
            favorites.Insert(0, item);
        }

        Save();
    }

    public void EditLabel(HistoryItem item, string? label)
    {
        item.Label = string.IsNullOrWhiteSpace(label)
            ? null
            : label.Trim();
        Save();
    }

    public void Delete(HistoryItem item)
    {
        items.Remove(item);
        favorites.Remove(item);
        Save();
    }

    /// <summary>Clears the ordinary items. Favorites survive.</summary>
    public void ClearNonFavorites()
    {
        items.Clear();
        Save();
    }

    /// <summary>Case-insensitive match over the query text, label, and operation name.</summary>
    public static bool Matches(HistoryItem item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return item.Query.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               item.Label?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true ||
               item.OperationName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>The list text: label, else operation name, else the query condensed to one line
    /// with comment lines stripped.</summary>
    public static string DisplayText(HistoryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Label))
        {
            return item.Label;
        }

        if (!string.IsNullOrWhiteSpace(item.OperationName))
        {
            return item.OperationName;
        }

        var condensed = string.Join(' ', item.Query
            .Split('\n')
            .Select(_ => _.Trim())
            .Where(_ => _.Length > 0 && !_.StartsWith('#'))
            .SelectMany(_ => _.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)));
        if (condensed.Length == 0)
        {
            return "<empty>";
        }

        return condensed;
    }
}
