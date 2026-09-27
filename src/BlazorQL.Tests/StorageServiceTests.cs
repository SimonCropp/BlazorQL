
public class StorageServiceTests
{
    [Test]
    public async Task NamespacesEveryKey()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend, "custom");
        storage.Set("query", "{ id }");

        await Assert.That(backend.Get("custom:query")).IsEqualTo("{ id }");
        await Assert.That(storage.Get("query")).IsEqualTo("{ id }");
    }

    [Test]
    public async Task CorruptValueIsRemovedAndReadsAsNull()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend);
        backend.Set("blazorql:theme", "null");
        backend.Set("blazorql:query", "undefined");

        await Assert.That(storage.Get("theme")).IsNull();
        await Assert.That(storage.Get("query")).IsNull();
        await Assert.That(backend.Get("blazorql:theme")).IsNull();
        await Assert.That(backend.Get("blazorql:query")).IsNull();
    }

    [Test]
    public async Task SettingEmptyRemovesTheKey()
    {
        var backend = new InMemoryStorageBackend();
        var storage = new StorageService(backend);
        storage.Set("query", "{ id }");
        storage.Set("query", "");

        await Assert.That(backend.Keys()).IsEmpty();
        await Assert.That(storage.Get("query")).IsNull();
    }

    [Test]
    public async Task ClearOnlyRemovesNamespacedKeys()
    {
        var backend = new InMemoryStorageBackend();
        backend.Set("other-app:token", "keep");
        backend.Set("blazorqlish", "keep-too");
        var storage = new StorageService(backend);
        storage.Set("query", "{ id }");
        storage.Set("theme", "dark");

        storage.Clear();

        await Assert.That(backend.Get("other-app:token")).IsEqualTo("keep");
        await Assert.That(backend.Get("blazorqlish")).IsEqualTo("keep-too");
        await Assert.That(storage.Get("query")).IsNull();
        await Assert.That(storage.Get("theme")).IsNull();
    }

    [Test]
    public async Task SetReportsBackendRefusal()
    {
        var storage = new StorageService(new RefusingBackend());

        await Assert.That(storage.Set("query", "{ id }")).IsFalse();
        // Empty means remove, which cannot fail.
        await Assert.That(storage.Set("query", "")).IsTrue();
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