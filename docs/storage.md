# Storage

State persists to localStorage under namespaced keys, configurable via the `StorageNamespace` parameter. The settings dialog's **Clear data** removes only namespaced keys, never the host app's.

The prefix is `blazorql:` for the `<BlazorQLIde/>` component. A bundled mount derives its own from the app and the mount instead — an app named `Orders` at the default `/blazorql` writes `blazorql/Orders/blazorql:`.

That derivation exists because localStorage is scoped to an **origin**, not to a path: every IDE ever served from a host shares one store, so a constant prefix means an IDE opens whatever tabs the last one left. The mount alone is not enough to separate them — two services that each take the default mount on the default port, the ordinary way to run two backends locally, would still land on the same keys — so the app carries it and the mount separates two mounts within that app.

The app is `IHostEnvironment.ApplicationName`, which is the entry assembly's name unless the app sets the `applicationName` configuration key. Name a `StorageNamespace` to keep storage across an assembly rename, or to share one deliberately between two mounts.

| Key | Contents |
| --- | --- |
| `blazorql:query` / `variables` / `headers` | The active tab's editors (headers only when persist-headers is on). |
| `blazorql:tabState` | All tabs and the active index. Responses are never persisted; per-tab headers only behind the opt-in. |
| `blazorql:shouldPersistHeaders` | The opt-in itself. |
| `blazorql:theme` | `light` or `dark`; absent means system. |
| `blazorql:visiblePlugin` | Which sidebar pane is open. |
| `blazorql:docExplorerFlex` / `editorFlex` / `secondaryEditorFlex` | Pane sizes. |
| `blazorql:queries` / `favorites` | History and favorites. |

Writes are debounced (~500 ms). Quota failures are swallowed — a debug convenience never fails the app.


## Why persist-headers is opt-in

Headers routinely carry bearer tokens and API keys; localStorage is readable by any script on the origin and survives indefinitely. The setting defaults off, and the dialog says what turning it on means. Turning it back off scrubs: the stored headers key is removed and every persisted tab's headers are nulled.
