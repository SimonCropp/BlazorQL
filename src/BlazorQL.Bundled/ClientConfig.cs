using System.Text.Json.Serialization;

/// <summary>
/// The subset of <see cref="BlazorQLIdeOptions"/> the browser needs, in the shape the WebAssembly
/// host reads. Serialized into index.html as <c>window.blazorqlConfig</c>.
/// </summary>
sealed record ClientConfig(
    string Endpoint,
    string? SubscriptionEndpoint,
    string? DefaultQuery,
    string? DefaultHeaders,
    bool IsHeadersEditorEnabled,
    bool ShouldPersistHeaders,
    int MaxHistoryLength,
    string StorageNamespace,
    string DefaultTheme,
    string? ForcedTheme);

/// <summary>Source-generated so the package stays trim- and AOT-clean for its consumers.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClientConfig))]
partial class IdeJson : JsonSerializerContext;
