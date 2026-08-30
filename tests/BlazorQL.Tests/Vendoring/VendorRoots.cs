/// <summary>
/// The pinned npm packages the vendored JS layer is bundled from, and the entry files that become
/// the vendored bundles. These are the version pins of the whole editor stack — monaco-editor
/// 0.52.2 is the ceiling monaco-graphql 1.8.0 supports (peer &lt; 0.53), and graphql is one version
/// everywhere (the page bundle carries one instance; each worker bundle carries its own, which is
/// fine — separate graphs). Change entries only in lockstep and re-run
/// <c>VendorTests.RefreshVendoredAssets</c>.
/// </summary>
static class VendorRoots
{
    /// <summary>npm package → exact version, downloaded as tarballs into the bundler's node_modules.</summary>
    public static IReadOnlyDictionary<string, string> Packages { get; } = new Dictionary<string, string>
    {
        ["monaco-editor"] = "0.52.2",
        ["monaco-graphql"] = "1.8.0",
        ["graphql"] = "17.0.2",
        ["graphql-language-service"] = "5.6.0",
        ["picomatch-browser"] = "2.2.6",
        ["debounce-promise"] = "3.1.2",
        ["nullthrows"] = "1.1.1",
        ["vscode-languageserver-types"] = "3.17.5",
        ["prettier"] = "3.3.2",
        ["jsonc-parser"] = "3.3.1",
    };

    /// <summary>The esbuild binary used to produce the bundles (win32-x64 — the refresh runs on dev machines).</summary>
    public const string EsbuildVersion = "0.25.5";

    /// <summary>Entry file (in src/BlazorQL/js) → vendored output name (in wwwroot/vendor).</summary>
    public static IReadOnlyDictionary<string, string> Entries { get; } = new Dictionary<string, string>
    {
        ["page-entry.js"] = "page.js",
        ["editor-worker-entry.js"] = "editor.worker.js",
        ["json-worker-entry.js"] = "json.worker.js",
        ["graphql-worker-entry.js"] = "graphql.worker.js",
        ["prettier-entry.js"] = "prettier.js",
    };

    /// <summary>Fetched raw, no bundling.</summary>
    public static IReadOnlyDictionary<string, string> RawFiles { get; } = new Dictionary<string, string>
    {
        // Fira Code with the font data embedded as data URIs (OFL licensed).
        ["fira-code.css"] = "https://esm.sh/@graphiql/react@0.38.0/font/fira-code.css",
    };
}
