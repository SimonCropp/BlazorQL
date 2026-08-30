[TestFixture]
public class StorageServiceTests
{
    [Test]
    public void NamespacesEveryKey()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend, "custom");
        storage.Set("query", "{ id }");

        Assert.That(backend.Get("custom:query"), Is.EqualTo("{ id }"));
        Assert.That(storage.Get("query"), Is.EqualTo("{ id }"));
    }

    [Test]
    public void CorruptValueIsRemovedAndReadsAsNull()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend);
        backend.Set("blazorql:theme", "null");
        backend.Set("blazorql:query", "undefined");

        Assert.That(storage.Get("theme"), Is.Null);
        Assert.That(storage.Get("query"), Is.Null);
        Assert.That(backend.Get("blazorql:theme"), Is.Null);
        Assert.That(backend.Get("blazorql:query"), Is.Null);
    }

    [Test]
    public void SettingEmptyRemovesTheKey()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend);
        storage.Set("query", "{ id }");
        storage.Set("query", "");

        Assert.That(backend.Keys(), Is.Empty);
        Assert.That(storage.Get("query"), Is.Null);
    }

    [Test]
    public void ClearOnlyRemovesNamespacedKeys()
    {
        var backend = new InMemoryStorageBackend();
        backend.Set("other-app:token", "keep");
        backend.Set("blazorqlish", "keep-too");
        var storage = new StorageService(backend);
        storage.Set("query", "{ id }");
        storage.Set("theme", "dark");

        storage.Clear();

        Assert.That(backend.Get("other-app:token"), Is.EqualTo("keep"));
        Assert.That(backend.Get("blazorqlish"), Is.EqualTo("keep-too"));
        Assert.That(storage.Get("query"), Is.Null);
        Assert.That(storage.Get("theme"), Is.Null);
    }

    [Test]
    public void SetReportsBackendRefusal()
    {
        var storage = new StorageService(new RefusingBackend());

        Assert.That(storage.Set("query", "{ id }"), Is.False);
        // Empty means remove, which cannot fail.
        Assert.That(storage.Set("query", ""), Is.True);
    }

    sealed class RefusingBackend :
        IStorageBackend
    {
        public string? Get(string key) => null;

        public bool Set(string key, string value) => false;

        public void Remove(string key)
        {
        }

        public IReadOnlyList<string> Keys() => [];
    }
}
