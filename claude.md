# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What BlazorQL is

An in-browser GraphQL IDE — a GraphiQL alternative — shipped as **two packages** built from one Razor Class Library. Everything lives under `src/`, including the tests and the sample.

- `src/BlazorQL` is the RCL, shipping the `<BlazorQLIde/>` component for apps that are already Blazor WebAssembly.
- `src/BlazorQL.Bundled` hosts that same IDE in any ASP.NET Core app from a single assembly — `app.MapBlazorQL("/graphql-ide", _ => _.Endpoint = "/graphql")`, no Blazor SDK, no static files, and **no package dependencies at all**. It embeds a published build of `src/BlazorQL.Bundled.Host` (a minimal WASM shell around the component) as brotli-compressed resources. See `docs/bundled.md`.

`src/BlazorQL.Sample` is a standalone Blazor WASM app that executes a schema **entirely client-side** (GraphQL.NET, `SampleSchema.cs`) and deploys to GitHub Pages. It is routed: `/` (`Pages/Home.razor`) is an ordinary consuming app — load-time query, mutation, subscription — demonstrating the debug sidecar, and `/explorer` (`Pages/Explorer.razor`) hosts the IDE with the endpoint bar. Both resolve the one DI-registered sidecar-wrapped `IGraphQLFetcher`. The `/explorer` deep link survives static hosting because `.github/workflows/pages.yml` copies `index.html` over `404.html`.

The RCL contains **no JS libraries**. The editors are Monaco via the **BlazorMonaco 3.5.0** NuGet (`<StandaloneCodeEditor>` components; the host page loads Monaco's AMD bundle before Blazor starts — see the sample's `wwwroot/index.html` for the required script ordering and why `Blazor.start()` is called by hand). Every language feature runs in C# in `src/BlazorQL/Language/`: completion (`CompletionEngine` + `ContextScanner`), validation (`SchemaValidator` — GraphQL.NET spec rules + a deprecation walker), hover (`HoverEngine`), variables checking (`VariablesChecker`), formatting (`Formatter`), fragment merging (`FragmentMerger`), leaf filling (`LeafFiller`), SDL printing (`SdlPrinter`), and jump-to-doc resolution (`SchemaReferenceResolver`), all over `GraphQLParser` and the `SchemaIndex` introspection model. `src/BlazorQL/wwwroot/blazorql.js` is pure utilities only (clipboard, downloads, hash, storage, theme attribute, pane-drag pointer tracking, global shortcuts) — never put editor or language logic there. All other behavior (layout, tabs, doc explorer, history, storage, fetchers) is C#.

The debug sidecar (`src/BlazorQL/Sidecar/`) is a self-contained opt-in area: `SidecarFetcher` decorates any `IGraphQLFetcher` and records into the singleton `SidecarStore`; `<BlazorQLSidecar/>` renders the panel and loads its own collocated JS module and `blazorql-sidecar.css` (injected at runtime, deliberately not scoped CSS). The IDE's status footer unwraps `SidecarFetcher.Inner` to reach `HttpFetcher.LastStatus` — keep that unwrap when adding decorators.

Each editor is moved onto a named model at init (`inmemory://model/blazorql-*`), which is how tests and the (page-global, guarded-by-model-uri) completion/hover providers address individual editors.

## Build pipeline

Two MSBuild targets files carry most of the risk. Both have long comments explaining why; read them before editing.

- `src/MonacoAssets.targets` prunes BlazorMonaco's static web assets — the 14 locale bundles and the ts/css/html workers, ~10 MiB of 15. Deliberately a **denylist**, applied at build time rather than publish time, guarded by an expected-drop-count that errors when BlazorMonaco's layout changes. Do not convert it to an allowlist and do not drop basic-languages grammars: grammar loading is driven by the *schema's* content (a fenced `python` code block inside a description makes Monaco fetch the python grammar), so the reachable set cannot be enumerated.
- `src/BlazorQL.Bundled/BundledHost.targets` publishes `BlazorQL.Bundled.Host` **out of process** (`dotnet publish -c Release`) and embeds the output. `BlazorQL.Bundled.Host` is deliberately not in the solution, and the `ProjectReference` from `BlazorQL.Bundled` to the RCL is build-ordering only (`ReferenceOutputAssembly="false"`) so the package ships dependency-free. First build takes a minute; pass `-p:BlazorQLSkipHostPublish=true` to skip it when iterating elsewhere.

## Build and test

One solution, no ordering constraints:

```bash
dotnet build src/BlazorQL.slnx
dotnet test src/BlazorQL.slnx
dotnet run --project src/BlazorQL.Sample
```

- `src/BlazorQL.Tests` — unit + bUnit (JS interop stubbed). Building it also runs MarkdownSnippets over `readme.md`/`docs/*.md` — never hand-edit inside snippet regions.
- `src/BlazorQL.Sample.Tests` — Playwright browser tests over the **published** sample, served by an in-process static host at `/` and (separately) under `/BlazorQL/` to prove GitHub Pages base-path safety. Verify screenshot baselines (`*.verified.png`) are the images the docs embed.
- `src/BlazorQL.Bundled.Tests` — Playwright over the bundled package mounted in a real ASP.NET Core host, which is the only suite where the schema runs **server-side** (the sample's runs in the browser). Also covers serving concerns: brotli, path bases, response compression. It links `SampleSchema.cs` from the sample rather than copying it, so the two suites cannot drift.

## Code conventions

- Public types in namespace `BlazorQL`; internal helpers in the global namespace with no namespace declaration.
- Lambda parameters are `_` (even when used); nested lambdas that would shadow get descriptive names. `Cancel` is the alias for `CancellationToken`.
- Line comments on their own line above the code, never trailing. `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` — keep it clean.
- Central Package Management: never version a `PackageReference`; add the `PackageVersion` to `src/Directory.Packages.props`.
- Storage keys are namespaced — `blazorql:` by default, configurable per instance via the `StorageNamespace` parameter (which is what isolates two bundled mounts from each other). One `<BlazorQLIde/>` per page remains a documented limitation: the completion and hover providers are registered page-globally and routed by model uri.
