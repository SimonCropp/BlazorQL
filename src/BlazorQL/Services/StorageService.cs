namespace BlazorQL;

/// <summary>
/// Where <see cref="StorageService"/> keeps its values. The browser backend is localStorage via
/// the host module; tests use <see cref="InMemoryStorageBackend"/>.
/// </summary>
public interface IStorageBackend
{
    string? Get(string key);

    /// <summary>False when the value could not be stored — a full quota, or storage disabled.</summary>
    bool Set(string key, string value);

    void Remove(string key);

    IReadOnlyList<string> Keys();
}

/// <summary>A dictionary-backed backend for tests and non-browser hosts.</summary>
public sealed class InMemoryStorageBackend :
    IStorageBackend
{
    readonly Dictionary<string, string> values = [];

    public string? Get(string key) =>
        values.GetValueOrDefault(key);

    public bool Set(string key, string value)
    {
        values[key] = value;
        return true;
    }

    public void Remove(string key) =>
        values.Remove(key);

    public IReadOnlyList<string> Keys() =>
        [.. values.Keys];
}

/// <summary>
/// Namespaced persistent storage, mirroring GraphiQL's StorageAPI: every key is stored as
/// <c>{ns}:{key}</c>, corrupt values self-heal, setting empty removes, and Clear only touches this
/// namespace.
/// </summary>
public sealed class StorageService(IStorageBackend backend, string ns = "blazorql")
{
    string Prefix => $"{ns}:";

    string FullKey(string key) =>
        Prefix + key;

    public string? Get(string key)
    {
        var value = backend.Get(FullKey(key));
        if (value is null)
        {
            return null;
        }

        // A literal "null"/"undefined" is a serialization accident from a previous session;
        // treat it as corrupt and heal the slot.
        if (value is "null" or "undefined")
        {
            backend.Remove(FullKey(key));
            return null;
        }

        return value;
    }

    /// <summary>Stores the value. Empty removes the key. False = quota exceeded or storage refused.</summary>
    public bool Set(string key, string value)
    {
        if (value.Length == 0)
        {
            backend.Remove(FullKey(key));
            return true;
        }

        return backend.Set(FullKey(key), value);
    }

    public void Remove(string key) =>
        backend.Remove(FullKey(key));

    /// <summary>Removes every key in this namespace, and nothing outside it.</summary>
    public void Clear()
    {
        foreach (var key in backend.Keys().Where(_ => _.StartsWith(Prefix, StringComparison.Ordinal)))
        {
            backend.Remove(key);
        }
    }
}
