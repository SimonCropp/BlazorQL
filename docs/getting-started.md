# Getting started

BlazorQL ships as a Razor Class Library for Blazor WebAssembly. One component, one required parameter.


## Install

```
dotnet add package BlazorQL
```


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

One `<BlazorQLIde/>` per page.
