using System.Collections.Frozen;
using System.Reflection;

/// <summary>
/// Every embedded file, indexed by the path it is served at. Built once per assembly rather than
/// per mount, because the payload is frozen at build time and two mounts serve identical bytes.
/// </summary>
static class IdeAssets
{
    const string prefix = "BlazorQL.Ide/";

    /// <summary>
    /// Verified against the sdk's own endpoint manifest for the whole payload. The publish output
    /// contains only js, wasm, css and html; the rest are here so that adding an asset later does
    /// not silently serve it as a download.
    /// </summary>
    static readonly FrozenDictionary<string, string> contentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".js"] = "text/javascript",
        [".wasm"] = "application/wasm",
        [".css"] = "text/css",
        [".html"] = "text/html",
        [".json"] = "application/json",
        [".dat"] = "application/octet-stream",
        [".map"] = "application/json",
        [".woff2"] = "font/woff2",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The two framework files that keep a stable name across builds. Everything else under
    /// _framework carries a fingerprint, so its url changes whenever its bytes do.
    /// </summary>
    static readonly string[] unfingerprinted = ["_framework/dotnet.js", "_framework/blazor.webassembly.js"];

    public static readonly FrozenDictionary<string, IdeAsset> ByRoute = Load();

    /// <summary>The raw index.html, before the per-request base href and config are patched in.</summary>
    public static readonly string IndexHtml = LoadIndex();

    static FrozenDictionary<string, IdeAsset> Load()
    {
        var assembly = typeof(IdeAssets).Assembly;
        // Frozen for the life of the assembly, so it identifies these bytes without hashing them.
        var seed = Convert.ToHexString(assembly.ManifestModule.ModuleVersionId.ToByteArray());
        var assets = new Dictionary<string, IdeAsset>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(".br", StringComparison.Ordinal))
            {
                continue;
            }

            // MSBuild builds the logical name from %(RecursiveDir), which is backslash-separated on
            // windows and forward-slash on unix, so the resource names differ by build host.
            var route = name[prefix.Length..^".br".Length].Replace('\\', '/');
            var immutable = route.StartsWith("_framework/", StringComparison.Ordinal) &&
                            !unfingerprinted.Contains(route, StringComparer.OrdinalIgnoreCase);

            assets[route] = new(assembly, name, route, ContentType(route), immutable, $"{seed}-{assets.Count}");
        }

        return assets.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    static string LoadIndex()
    {
        var assembly = typeof(IdeAssets).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{prefix}index.html");
        if (stream is null)
        {
            throw new InvalidOperationException(
                "BlazorQL.Bundled was built without its embedded IDE. The BundledHost.targets publish step did not run.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    static string ContentType(string route) =>
        contentTypes.GetValueOrDefault(Path.GetExtension(route), "application/octet-stream");
}
