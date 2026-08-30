# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What BlazorQL is

An in-browser GraphQL IDE for Blazor — a GraphiQL alternative. `src/BlazorQL` is a Razor Class Library shipping the `<BlazorQL/>` component; `samples/BlazorQL.Sample` is a standalone Blazor WASM app that executes a schema **entirely client-side** (GraphQL.NET, `SampleSchema.cs`) and deploys to GitHub Pages. It is routed: `/` (`Pages/Home.razor`) is an ordinary consuming app — load-time query, mutation, subscription — demonstrating the debug sidecar, and `/explorer` (`Pages/Explorer.razor`) hosts the IDE with the endpoint bar. Both resolve the one DI-registered sidecar-wrapped `IGraphQLFetcher`, and the Pages 404 fallback is what makes the `/explorer` deep link work on static hosting. Feature parity target is GraphiQL 5.3 (see `docs/`).

The RCL contains **no JS libraries**. The editors are Monaco via the **BlazorMonaco 3.5.0** NuGet (`<StandaloneCodeEditor>` components; the host page loads Monaco's AMD bundle before Blazor starts — see the sample's `wwwroot/index.html` for the required script ordering). Every language feature runs in C# in `src/BlazorQL/Language/`: completion (`CompletionEngine` + `ContextScanner`), validation (`SchemaValidator` — GraphQL.NET spec rules + a deprecation walker), hover (`HoverEngine`), variables checking (`VariablesChecker`), formatting (`Formatter`), fragment merging (`FragmentMerger`), leaf filling (`LeafFiller`), SDL printing (`SdlPrinter`), and jump-to-doc resolution (`SchemaReferenceResolver`), all over `GraphQLParser` and the `SchemaIndex` introspection model. `src/BlazorQL/wwwroot/blazorql.js` is pure utilities only (clipboard, downloads, hash, storage, theme attribute, pane-drag pointer tracking, global shortcuts) — never put editor or language logic there. All other behavior (layout, tabs, doc explorer, history, storage, fetchers) is C#.

The debug sidecar (`src/BlazorQL/Sidecar/`) is a self-contained opt-in area: `SidecarFetcher` decorates any `IGraphQLFetcher` and records into the singleton `SidecarStore`; `<BlazorQLSidecar/>` renders the panel and loads its own collocated JS module and `blazorql-sidecar.css` (injected at runtime, deliberately not scoped CSS). The IDE's status footer unwraps `SidecarFetcher.Inner` to reach `HttpFetcher.LastStatus` — keep that unwrap when adding decorators.

Each editor is moved onto a named model at init (`inmemory://model/blazorql-*`), which is how tests and the (page-global, guarded-by-model-uri) completion/hover providers address individual editors.

## Build and test

One solution, no ordering constraints:

```bash
dotnet build BlazorQL.slnx
dotnet test BlazorQL.slnx
dotnet run --project samples/BlazorQL.Sample
```

- `tests/BlazorQL.Tests` — unit + bUnit (JS interop stubbed). Building it also runs MarkdownSnippets over `readme.md`/`docs/*.md` — never hand-edit inside snippet regions.
- `tests/BlazorQL.Sample.Tests` — Playwright browser tests over the **published** sample, served by an in-process static host at `/` and (separately) under `/BlazorQL/` to prove GitHub Pages base-path safety. Verify screenshot baselines (`*.verified.png`) are the images the docs embed.

## Code conventions

- Public types in namespace `BlazorQL`; internal helpers in the global namespace with no namespace declaration.
- Lambda parameters are `_` (even when used); nested lambdas that would shadow get descriptive names. `Cancel` is the alias for `CancellationToken`.
- Line comments on their own line above the code, never trailing. `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` — keep it clean.
- Central Package Management: never version a `PackageReference`; add the `PackageVersion` to `Directory.Packages.props`.
- Storage keys are namespaced `blazorql:*` (single-instance per page in v1 — documented limitation).
