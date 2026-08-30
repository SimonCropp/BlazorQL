/// <summary>
/// The pinned esm.sh entry points the vendored JS layer is built from. These are the version pins
/// of the whole editor stack — monaco-editor 0.52.2 is the ceiling monaco-graphql 1.8.0 supports
/// (peer &lt; 0.53), and graphql must be a single version per JS graph (the page graph externals it;
/// the worker graph bundles its own copy, deliberately). Change entries only in lockstep and
/// re-run <c>VendorTests.RefreshVendoredAssets</c>.
/// </summary>
static class VendorRoots
{
    /// <summary>Entry files fetched and BFS-walked, keyed by the local file name they land as.</summary>
    public static IReadOnlyDictionary<string, string> Modules { get; } = new Dictionary<string, string>
    {
        // The one graphql instance of the page graph; everything else marks it external.
        ["graphql.js"] = "https://esm.sh/graphql@17.0.2?target=es2022",
        // monaco-graphql's trimmed monaco entry (graphql + json languages + edcore) — the one true
        // Monaco instance the host module publishes as globalThis.monaco.
        ["monaco-editor.js"] = "https://esm.sh/monaco-graphql@1.8.0/monaco-editor?target=es2022&deps=monaco-editor@0.52.2",
        ["monaco-graphql.js"] = "https://esm.sh/monaco-graphql@1.8.0/initializeMode?target=es2022&deps=monaco-editor@0.52.2&external=graphql",
        ["graphql-language-service.js"] = "https://esm.sh/graphql-language-service@5.6.0?target=es2022&external=graphql",
        ["editor.worker.js"] = "https://esm.sh/monaco-editor@0.52.2/esm/vs/editor/editor.worker.js?target=es2022",
        ["json.worker.js"] = "https://esm.sh/monaco-editor@0.52.2/esm/vs/language/json/json.worker.js?target=es2022",
        // Self-contained: a worker cannot share the page's modules, so this graph carries its own
        // graphql, pinned to the same version.
        ["graphql.worker.js"] = "https://esm.sh/monaco-graphql@1.8.0/esm/graphql.worker.js?target=es2022&deps=monaco-editor@0.52.2,graphql@17.0.2",
        ["prettier-standalone.js"] = "https://esm.sh/prettier@3.3.2/standalone?target=es2022",
        ["prettier-graphql.js"] = "https://esm.sh/prettier@3.3.2/plugins/graphql?target=es2022",
        ["prettier-estree.js"] = "https://esm.sh/prettier@3.3.2/plugins/estree?target=es2022",
        ["prettier-babel.js"] = "https://esm.sh/prettier@3.3.2/plugins/babel?target=es2022",
        ["jsonc-parser.js"] = "https://esm.sh/jsonc-parser@3.3.1?target=es2022",
    };

    /// <summary>Fetched raw, no import walking.</summary>
    public static IReadOnlyDictionary<string, string> RawFiles { get; } = new Dictionary<string, string>
    {
        // Fira Code with the font data embedded as data URIs (OFL licensed).
        ["fira-code.css"] = "https://esm.sh/@graphiql/react@0.38.0/font/fira-code.css",
    };
}
