# Getting started

BlazorQL ships as a Razor Class Library for Blazor WebAssembly. One component, one required parameter.


## Install

```
dotnet add package BlazorQL
```


## Wire up index.html

The editors are Monaco, delivered by the BlazorMonaco package the component depends on. The host page links the stylesheets and loads Monaco before Blazor starts (the editor components need `window.monaco` the moment they first render):

```html
<head>
    ...
    <link rel="stylesheet" href="_content/BlazorQL/blazorql.css" />
    <link rel="stylesheet" href="_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.css" />
</head>
<body>
    ...
    <script src="_content/BlazorMonaco/jsInterop.js"></script>
    <script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/loader.js"></script>
    <script src="_content/BlazorMonaco/lib/monaco-editor/min/vs/editor/editor.main.js"></script>
    <script src="_framework/blazor.webassembly.js" autostart="false"></script>
    <script src="_content/BlazorQL/blazorql-boot.js"></script>
</body>
```

`blazorql-boot.js` is a one-liner shipped in the package: `autostart="false"` plus its `require` call removes the race between the AMD loader publishing Monaco and Blazor rendering the first editor. It is a file rather than an inline script so the page runs under `script-src 'self'` — inline it instead and the app needs `'unsafe-inline'` or a nonce. See the sample's `wwwroot/index.html` for the full page.

If the app sends a `Content-Security-Policy` header, it needs widening before any of this runs — a `'self'` policy blocks the .NET runtime, Monaco's icon font and its language workers. See [Content Security Policy](csp.md).


## Render the IDE

Pick a fetcher and hand it to the component. Against an HTTP endpoint:

```razor
@using BlazorQL

<BlazorQLIde Fetcher="fetcher" />

@code {
    readonly IGraphQLFetcher fetcher = new HttpFetcher("https://example.com/graphql");
}
```

The component fills its container — give it a full-height parent. The sample wraps it like this:

```css
.sample-shell {
    display: flex;
    flex-direction: column;
    height: 100%;
}
```

On boot the component introspects through the fetcher, feeds the schema to the editors, and everything lights up: completion, validation, docs, execution.


## Parameters

| Parameter | Default | Purpose |
| --- | --- | --- |
| `Fetcher` (required) | — | Transports requests, introspection included. |
| `DefaultQuery` | welcome text | Seed for the first tab. |
| `DefaultHeaders` | — | Headers seed for new tabs. |
| `ShouldPersistHeaders` | `false` | Persist the headers editor across reloads (opt-in — headers often carry tokens). |
| `IsHeadersEditorEnabled` | `true` | `false` hides the Headers tab entirely. |
| `ForcedTheme` / `DefaultTheme` | system | Pin or seed the theme. |
| `MaxHistoryLength` | 20 | Non-favorite history cap. |
| `StorageNamespace` | `blazorql` | localStorage key prefix. |
| `ConfirmCloseTab` | — | Async veto for tab closes. |
| `Logo` / `ToolbarContent` / `FooterContent` | — | Render fragments for the header logo, extra toolbar buttons, and the response footer. |

One `<BlazorQLIde/>` per page. The language providers are registered with the page's monaco and routed to the live instance by model uri, so a second one on the same page would answer for the first. The registration is per JS runtime, which under Blazor Server means per circuit rather than per process.
