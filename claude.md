# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What BlazorQL is

An in-browser GraphQL IDE for Blazor — a GraphiQL alternative. `src/BlazorQL` is a Razor Class Library shipping the `<BlazorQL/>` component; `samples/BlazorQL.Sample` is a standalone Blazor WASM app that executes a schema **entirely client-side** (vendored graphql-js) and deploys to GitHub Pages. Feature parity target is GraphiQL 5.3 (see `docs/`).

The editor layer is GraphiQL's own: `monaco-editor@0.52.2` + `monaco-graphql` + `graphql-js@17.0.2` vendored as esm.sh-built ESM under `src/BlazorQL/wwwroot/vendor/` (committed — builds never touch the network) behind one hand-written host module `src/BlazorQL/wwwroot/blazorql.js`. **There is exactly one Monaco instance** (`globalThis.monaco`, the vendored `monaco-editor.js` entry) and exactly one `graphql` module per JS graph — never introduce a second copy, and never add a bundler. All other behavior (layout, tabs, doc explorer, history, storage, fetchers) is C#.

## Build and test

One solution, no ordering constraints:

```bash
dotnet build BlazorQL.slnx
dotnet test BlazorQL.slnx
dotnet run --project samples/BlazorQL.Sample
```

- `tests/BlazorQL.Tests` — unit + bUnit (JS interop stubbed). Building it also runs MarkdownSnippets over `readme.md`/`docs/*.md` — never hand-edit inside snippet regions.
- `tests/BlazorQL.Sample.Tests` — Playwright browser tests over the **published** sample, served by an in-process static host at `/` and (separately) under `/BlazorQL/` to prove GitHub Pages base-path safety. Verify screenshot baselines (`*.verified.png`) are the images the docs embed.
- Vendored-asset integrity is a normal test (`VendorManifestTests`); refreshing the vendored files is the NUnit `[Explicit]` test `VendorTests.RefreshVendoredAssets` (network; overwrites `wwwroot/vendor/` per the pinned manifest — review the git diff it produces).

## Version pins that must move in lockstep

`monaco-editor` 0.52.2 ↔ `monaco-graphql` 1.8.0 (peer `< 0.53`); `graphql` 17.0.2 everywhere in a JS graph (the worker carries its own copy — that is deliberate; page and worker are separate graphs). Pins live in the vendor manifest inside `tests/BlazorQL.Tests` — change them only together and re-run the refresh test.

## Code conventions

- Public types in namespace `BlazorQL`; internal helpers in the global namespace with no namespace declaration.
- Lambda parameters are `_` (even when used); nested lambdas that would shadow get descriptive names. `Cancel` is the alias for `CancellationToken`.
- Line comments on their own line above the code, never trailing. `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` — keep it clean.
- Central Package Management: never version a `PackageReference`; add the `PackageVersion` to `Directory.Packages.props`.
- Storage keys are namespaced `blazorql:*` (single-instance per page in v1 — documented limitation).
