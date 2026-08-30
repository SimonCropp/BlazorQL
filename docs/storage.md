# Storage

State persists to localStorage under namespaced keys — `blazorql:` by default, configurable via the `StorageNamespace` parameter. The settings dialog's **Clear data** removes only namespaced keys, never the host app's.

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
