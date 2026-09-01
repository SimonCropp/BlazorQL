/// <summary>
/// One embedded file, served straight out of the assembly's resource table. The brotli bytes are
/// what is stored; identity bytes are decompressed on demand and cached under a cap.
/// </summary>
sealed class IdeAsset(Assembly assembly, string resourceName, string route, string contentType, bool immutable, string etagSeed)
{
    /// <summary>The path under the mount, e.g. <c>_framework/dotnet.js</c>.</summary>
    public string Route { get; } = route;

    public string ContentType { get; } = contentType;

    /// <summary>
    /// Fingerprinted assets never change under their url, so they get a year. Everything else
    /// revalidates, which costs a 304 - and the framework files blazor actually cares about carry
    /// "cache": "force-cache" in the boot config, so they do not even do that.
    /// </summary>
    public string CacheControl { get; } = immutable ? "max-age=31536000, immutable" : "no-cache";

    /// <summary>
    /// A strong validator with no hashing. The payload is frozen at build time, so the module
    /// version id plus the resource name identifies these bytes for the life of the assembly. The
    /// content coding is part of it because an etag identifies a representation, not a file.
    /// </summary>
    public string ETag { get; } = $"\"{etagSeed}-b\"";

    public string IdentityETag { get; } = $"\"{etagSeed}-i\"";

    byte[]? identity;

    public Stream OpenCompressed() =>
        assembly.GetManifestResourceStream(resourceName)!;

    public long CompressedLength
    {
        get
        {
            using var stream = OpenCompressed();
            return stream.Length;
        }
    }

    /// <summary>
    /// The decompressed bytes, for a client that will not take brotli. Cached after the first ask:
    /// this is the rare path, but a crawler hammering it should not re-inflate every time.
    /// </summary>
    public byte[] Identity()
    {
        if (identity is not null)
        {
            return identity;
        }

        using var compressed = OpenCompressed();
        using var brotli = new BrotliStream(compressed, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        brotli.CopyTo(buffer);
        return identity = buffer.ToArray();
    }
}
