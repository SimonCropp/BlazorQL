# TODO: bugs and performance improvements

Findings from a read-through of `src/BlazorQL`, the sample, and the bundled host. Items marked
**repro confirmed** were reproduced by running the built `BlazorQL.dll` from a throwaway console
project (not checked in); the rest are from code reading. Line numbers are as of commit 3351649.

Not listed here: the validator rules deliberately left out (see `knownGaps` in
`ValidatorParityTests`), the swallowed storage-quota failures (`docs/storage.md`), and the
one-instance-per-page limitation. Those are documented choices, not defects.


## Bugs

### High

- [x] **Merge fragments crashes the app on a fragment cycle** (repro confirmed)
  `src/BlazorQL/Language/FragmentMerger.cs:51-61`, `:125-128`
  `Flatten` creates a fresh `seenSpreads` for every selection set, so the guard never sees a
  spread that is already being inlined further up. `Deduplicate` then calls `Flatten` on the
  inline fragment it produced, and the recursion never ends. `{ person { ...F } } fragment F
  on Person { name ...F }` and an `A <-> B` pair both end in `StackOverflowException`. In WebAssembly
  a stack overflow takes the whole runtime down, so Shift-Ctrl-M or the merge button on such a
  document is a page reload. Cycles are not caught earlier because `NoFragmentCycles` is a known
  validator gap, so nothing stops the merge from being attempted.
  Fix: thread the set of fragment names currently being inlined (the ancestor path) through
  `Flatten`/`Inline` and skip a spread that is already on the path, or run a cycle check first
  and report `Cannot spread fragment "F" within itself` the way graphql-js does.

- [x] **Remove-field crashes the app on a fragment cycle** (repro confirmed)
  `src/BlazorQL/Language/FieldRemover.cs:112-119`
  `Resolve` follows spreads without advancing `depth` and without a visited set. A path that does
  not resolve keeps re-entering the cycle until the stack is gone. Lower exposure than the merge
  case because it needs a response error with a path, but the same crash. Carry a `HashSet<string>`
  of fragments on the current path and stop on re-entry.

- [x] **Validator reports variables used only inside fragments as "never used" / "not defined"**
  (repro confirmed)
  `src/BlazorQL/Language/SchemaValidator.cs:110-149`, `:315-325`, `:72-75`
  `WalkOperation` only records variable usages it sees in the operation body; `WalkSpread` records
  the spread name and does not descend. So `query Q($t: String!) { ...F } fragment F on Query {
  search(term: $t) { __typename } }` gets `Variable "$t" is never used.` This is the Relay-style
  shape most real documents have. Worse, fragments are walked after all operations with whatever
  the *last* operation left in `variableDefinitions`, so with two operations the fragment also
  gets `Variable "$a" is not defined.` A document that is only fragments reports every variable as
  undefined. Both are red squiggles on valid documents, and the parity corpus has no case covering
  a variable used inside a fragment body, which is why the test does not catch it.
  Fix: per operation, collect the recursively referenced fragments (spreads, with a visited set)
  and union their variable usages before computing unused/undefined, as graphql-js's
  `getRecursiveVariableUsages` does; do not run the variable checks during the standalone fragment
  walk. Add the two documents above to `ValidatorParityTests.Corpus`.

- [x] **Introspection is sent without the headers editor's headers**
  `src/BlazorQL/BlazorQLIde.razor.cs:996-1004`
  `Introspect` passes `emptyHeaders`, so an endpoint that needs `Authorization` (most real ones)
  cannot be introspected, and "Re-fetch schema" after typing the header still sends nothing. The
  whole language layer stays dark against authenticated APIs even though every query runs fine.
  GraphiQL sends the headers editor's parsed contents with the introspection request.
  Fix: build the header dictionary the same way `Run` does (`ParseEditorJson` +
  `ToHeaderDictionary`) and pass it; at boot, before the editors exist, use `tabs.Active.Headers`.
  Related: with `IsHeadersEditorEnabled = false`, `DefaultHeaders` is stored on the tab but never
  sent by `Run` (`:1511-1522`) either.

- [x] **Schema descriptions can inject `javascript:` links into the IDE's origin** (repro confirmed)
  `src/BlazorQL/Components/DocExplorer/MarkdownView.razor.cs:11-15`,
  `src/BlazorQL/Components/DocExplorer/TypeDoc.razor:8`
  The Markdig pipeline disables raw HTML but does nothing about link targets:
  `[click me](javascript:alert(document.cookie))` renders as
  `<a href="javascript:alert(document.cookie)">`, and `![x](javascript:alert(1))` as an `<img src>`
  with the same scheme. `specifiedByURL` is rendered straight into an `<a href>` as well.
  Descriptions are endpoint-controlled, and the bundled package serves the IDE on the API's own
  origin, often with cookies. markdown-it (what GraphiQL uses) blocks `javascript:`, `vbscript:`
  and `data:` through `validateLink`; Markdig has no equivalent switch.
  Fix: before rendering, walk the `MarkdownDocument` for `LinkInline` nodes and drop any `Url`
  whose scheme is not http/https/mailto/relative (or install a custom `LinkInlineRenderer`); for
  `SpecifiedByURL`, only emit the anchor when `Uri.TryCreate` succeeds with an http(s) scheme.

### Medium

- [x] **Variables editor is checked against the first operation, not the one being run**
  `src/BlazorQL/BlazorQLIde.razor.cs:703-704`
  `CheckVariables` calls `document.OperationNode(null)`, which is always the first operation.
  With several operations in the document the picker/caret choice (`tabs.Active.OperationName`)
  is ignored, so the variables pane shows errors for the wrong operation's declarations. Pass
  `tabs.Active.OperationName` (or the operation at the caret).

- [x] **Completion and hover inside a directive's argument list show the enclosing field's arguments**
  (repro confirmed)
  `src/BlazorQL/Language/ContextScanner.cs:194-203`, `:276-281`,
  `src/BlazorQL/Language/HoverEngine.cs:42-46`
  `@name` is consumed without being remembered, so the `(` that follows pushes an `Arguments`
  frame carrying the field's `LastField`. `{ hasArgs @repeat(|` offers `string`, `count`, `input`,
  `deprecatedArg`; hovering `string` in `@repeat(string: 1)` shows the field argument's docs.
  `@include(if:` on a field with arguments is the everyday case. Remember the directive on `@name`
  (from `schema.Directives`) and push a frame that resolves against the directive's `Args`.

- [x] **Phantom fragment names from any text containing "fragment"** (repro confirmed)
  `src/BlazorQL/Language/ContextScanner.cs:524-548`
  `CollectFragments` uses `IndexOf("fragment")` with no word boundary and no awareness of comments
  or strings. `# see fragments here` yields a fragment named `s`; `{ fragmentCount }` yields
  `Count`. Both show up as spread completions after `...`. Require an identifier boundary on both
  sides and skip comments/strings, or take the names from the parsed document whenever it parses.

- [x] **Merge drops fragment definitions that are still spread** (repro confirmed)
  `src/BlazorQL/Language/FragmentMerger.cs:20-33`, `:70`
  Spreads carrying a directive are deliberately left in place, but every fragment definition is
  removed regardless. `{ person { ...F @include(if: $s) } } fragment F on Person { name }` merges to
  a document that spreads `F` with no `F`, i.e. one click turns a valid document into
  `Unknown fragment "F"`. GraphiQL's `mergeAst` has the same flaw; that is not a reason to keep it.
  After inlining, keep any definition whose name is still spread.

- [x] **Overlapping schema loads race; a stale schema can win**
  `src/BlazorQL/BlazorQLIde.razor.cs:221-233`, `:343-356`, `:954-993`, `:1028-1041`
  `OnParametersSetAsync` starts `LoadSchema` for a swapped-in fetcher without cancelling the one
  in flight; `RefetchSchema` and `EditorReady` can overlap it too. Apply endpoint A, then B before
  A's introspection returns: A's result lands last and `Schema`, `SchemaSdl` and `validator`
  describe A while requests go to B. Hold a per-load `CancelSource`, cancel it at the top of each
  `LoadSchema`, and ignore a result whose generation is not current.

- [x] **Two runs can overlap through the operation picker**
  `src/BlazorQL/BlazorQLIde.razor.cs:1437-1461`, `:1490-1495`, `:1545-1547`, `:1577-1594`
  `RunFromKeyboard` does not close the picker, and `RunPicked` has no `running` guard. Ctrl-Enter
  with the picker open, then click a picker entry: the second `Run` replaces `execution` without
  cancelling the first, and when the first finishes its `finally` disposes the second's token
  source, flips `running` off and writes the status line while the second is still streaming.
  Guard `RunPicked` on `running`, close the picker in `RunFromKeyboard`, and have `Run` cancel and
  await any execution already in flight. While here: `LoadActiveTab` clears `statusLine` (`:1298`)
  but the cancelled run's `finally` writes `stopped · N ms` afterwards, so the status of the old
  tab's run appears under the new tab.

- [x] **`VariablesChecker` throws on duplicate variable names and diagnostics go stale**
  (repro confirmed)
  `src/BlazorQL/Language/VariablesChecker.cs:13-14`, `src/BlazorQL/BlazorQLIde.razor.cs:680-685`
  `ToDictionary` throws `ArgumentException` for `query Q($v: Int, $v: Int)`. `RunDiagnostics`
  swallows it, so the variables pane keeps whatever markers it had before. Uniqueness is a known
  validator gap; the checker should still not throw. Use `TryAdd` or a first-wins lookup.

- [x] **Stopping a websocket subscription can hang the run's `finally`**
  `src/BlazorQL/Fetchers/GraphQLWsFetcher.cs:35-50`, `src/BlazorQL/Fetchers/GraphQLWsProtocol.cs:27-34`
  `CloseBestEffort` calls `CloseAsync` with `Cancel.None`, which waits for the server's close
  frame indefinitely. That runs inside the enumerator's disposal, which `Run` awaits before it
  sets `running = false`, so an unresponsive server leaves the stop button stuck. On WebAssembly a
  cancelled receive may abort the socket first (state `Aborted`, close skipped), so how often this
  bites depends on the runtime; either way it should be `CloseOutputAsync` or a close bounded by a
  short timeout.

### Low

- [x] **Toolbar and history actions dereference editors before they exist**
  `src/BlazorQL/BlazorQLIde.razor.cs:1407-1411`, `:1414-1422`, `:1765-1788`
  `CopyQuery`, `ShareQuery` and `LoadHistoryItem` use `operationEditor!`. The toolbar renders
  before Monaco initializes, and a persisted-open history pane does too, so a click before `ready`
  is a `NullReferenceException`. Gate on `ready` (or null-check like `PrettifyEditors` does).

- [x] **The status footer does not see through `SplitFetcher`**
  `src/BlazorQL/BlazorQLIde.razor.cs:1580-1589`, `src/BlazorQL.Bundled.Host/Shell.razor.cs:36`
  Only `SidecarFetcher.Inner` is unwrapped. The bundled host with `SubscriptionEndpoint` set wraps
  the `HttpFetcher` in a `SplitFetcher`, so that configuration never shows an HTTP status code.
  Add a small unwrap that walks `SidecarFetcher.Inner` and `SplitFetcher.Other`.

- [x] **Object URL is revoked synchronously after `click()`**
  `src/BlazorQL/wwwroot/blazorql.js:52-59`
  Chrome copes; Firefox and Safari have cancelled downloads when the blob URL is revoked before
  the click has been processed. Revoke on a `setTimeout` (or after `requestAnimationFrame`).

- [x] **`TabStore.Close` on the last tab leaves an empty store**
  `src/BlazorQL/Services/TabStore.cs:35-46`
  `ActiveIndex` becomes -1 and `Active` throws `ArgumentOutOfRangeException` (repro confirmed).
  The UI hides the close button for a single tab (`TabBar.razor:28`), so it is unreachable today,
  but the store should refuse or re-seed rather than rely on the markup.

- [x] **`HttpFetcher` silently drops user content headers and doubles up `Accept`**
  `src/BlazorQL/Fetchers/HttpFetcher.cs:34-38`
  `TryAddWithoutValidation` on request headers returns false for content headers such as
  `Content-Type`, so a user-supplied one is ignored without a word; a user `Accept` is appended
  after the built-in one. Route content headers to `message.Content.Headers` and let a user
  `Accept` replace the default.

- [x] **Failures inside debounced callbacks are unobserved**
  `src/BlazorQL/Services/Debouncer.cs:10-16`
  `RunAfterDelay` is fire-and-forget, so an exception from the action (a `GetValue` after the
  editor was torn down, a failed interop call) is lost. Catch and log, or route through
  `InvokeAsync` with a handler.

- [x] **Page-global statics assume one JS runtime**
  `src/BlazorQL/BlazorQLIde.razor.cs:111-112`
  `active` and `providersRegistered` are process-wide. Fine for WebAssembly (the documented host),
  but under Blazor Server the second circuit would never register providers and completions would
  route to whichever instance initialized last. Worth an explicit guard or a doc line.

- [x] **Rendered index cache keyed by base href is unbounded**
  `src/BlazorQL.Bundled/IdeEndpoint.cs:11`, `:112-114`
  Every distinct `PathBase` gets a cached page. With `UseForwardedHeaders` honouring
  `X-Forwarded-Prefix` from an untrusted hop, a client can grow this without limit. Cap it, or key
  on the resolved `BaseHref` only after validating it.


## Performance

- [ ] **Every pointer move during a pane drag re-renders the whole IDE**
  `src/BlazorQL/wwwroot/blazorql.js:115-140`, `src/BlazorQL/BlazorQLIde.razor.cs:1141-1180`
  `trackPointer` calls into .NET on every `pointermove` with no coalescing, and `OnPaneResize`
  calls `StateHasChanged` each time. Because every child component takes `EventCallback`
  parameters, Blazor re-renders the entire subtree on each of those renders, including the doc
  explorer's root page (see the next item) and the history list, at pointer-event rate.
  Coalesce in JS with `requestAnimationFrame` (send only the latest position per frame) and skip
  sending while a previous `invokeMethodAsync` is still pending; alternatively apply the flex
  style directly in JS during the drag and commit the ratio to C# on `pointerup`.

- [x] **`SchemaDoc` recomputes generate-ability and re-sorts every type on every render**
  `src/BlazorQL/Components/DocExplorer/SchemaDoc.razor:27-40`, `SchemaDoc.razor.cs:41-44`,
  `src/BlazorQL/Language/QueryGenerator.cs:24-36`
  `OtherTypes()` sorts all types per render, and `CanGenerateOperation` is called twice per type,
  each call running `RootFields` (a LINQ scan plus `ToList` over the query root's fields) and
  possibly `PathFromQuery`. That is O(types x rootFields) per render, and the item above makes
  renders frequent; on a GitHub-sized schema this is noticeable during a drag. Compute the sorted
  list and both flags once per `Schema` instance in `OnParametersSet`, or add a lazily built
  root-fields-by-return-type lookup to `SchemaIndex`.

- [x] **A large response is copied and parsed many times, including on every render**
  `src/BlazorQL/Services/IncrementalMerger.cs:22-23`, `:31-45`,
  `src/BlazorQL/BlazorQLIde.razor.cs:628-644`, `:1711-1717`, `:1728-1729`,
  `src/BlazorQL/BlazorQLIde.razor:195`
  Per payload: `JsonElement` -> `GetRawText()` string -> `JsonNode.Parse` tree -> indented
  `ToJsonString` -> marshalled to Monaco -> `OnResponseContentChanged` marshals the whole text
  back through `GetValue()`. Then `ResponseFieldErrors` is a property evaluated in the markup, so
  `ResponseErrors.Parse` re-parses the entire response with `JsonDocument.Parse` on every render
  of the IDE, including each pointer-move render and each subscription event.
  Fix: (a) for a plain single payload, write the indented JSON straight from the `JsonElement`
  with `Utf8JsonWriter` and skip the `JsonNode` tree; (b) record `tabs.Active.Response` inside
  `SetResponse` instead of reading it back through interop; (c) cache the parsed error list keyed
  on the response string reference.

- [x] **Linear member lookups throughout the language layer**
  `src/BlazorQL/Language/SchemaValidator.cs:221,281,306,385,394,508,535,553`,
  `src/BlazorQL/Language/ContextScanner.cs:359,371,383`, `LeafFiller.cs:72,127`,
  `HoverEngine.cs:30,43`, `VariablesChecker.cs:139,156`, `Components/DocExplorer/FieldDoc.razor.cs:34-37`
  Fields, input fields, enum values, arguments and directives are lists searched with
  `FirstOrDefault(_ => _.Name == name)`. Diagnostics run over the whole document on every
  keystroke (400 ms debounce), so a document over a type with hundreds of fields pays
  O(fields) per selected field, and the required-argument and required-input-field checks are
  O(declared x provided). Build `Dictionary<string, ...>` indexes lazily (on `SchemaIndex` or the
  introspection records) and use them everywhere.

- [ ] **Sidecar pretty-prints documents it will discard, and snapshots the log four times per render**
  `src/BlazorQL/Sidecar/SidecarFetcher.cs:68`, `SidecarStore.cs:20-29`,
  `BlazorQLSidecar.razor:19,24,31`, `BlazorQLSidecar.razor.cs:17-18`
  `Pretty(current)` runs before `AddDocument` checks `MaxDocumentsPerEntry`, so a long
  subscription with large events pays an indented serialization per event forever, and
  `store.Notify()` re-renders the panel per event. `Entries` copies the list under a lock on every
  access and the panel reads it four times per render. Check the cap before serializing, snapshot
  once per render, and consider a byte cap per entry: 100 entries x 25 documents of unbounded size
  is much memory for a debug panel.

- [ ] **Language providers re-fetch the model they already hold, and rescan offsets per marker**
  `src/BlazorQL/BlazorQLIde.razor.cs:411-412`, `:452-453`, `:491`, `:727-747`, `:1841-1889`
  `ProvideCompletions`, `ProvideOperationHover` and `ProvideResponseImageHover` call
  `Global.GetModel` and then `GetValue`, two interop round trips per request (space is a trigger
  character, so this is per word typed), when `operationModel`/`responseModel` already wrap the
  same model. `ToOffset`/`ToLineColumn` are O(n) scans repeated per diagnostic marker and per leaf
  decoration. Use the held `TextModel`, and compute line starts once per text.

- [x] **SDL is printed eagerly on every schema load and crosses interop twice**
  `src/BlazorQL/BlazorQLIde.razor.cs:983`, `src/BlazorQL/Components/DocExplorer/DocExplorer.razor.cs:352-360`, `:368-378`
  `SdlPrinter.Print` runs for every introspection whether or not the SDL view is ever opened, and
  when it is, `SdlOptions` passes the whole SDL as the editor's construction value and
  `OnSdlEditorInit` then creates the named model with the same text. Make `SchemaSdl` lazy
  (first toggle) and construct the editor with an empty value, as the other editors do.

- [x] **`ParseJsonc` leaks pooled `JsonDocument` buffers**
  `src/BlazorQL/Language/Formatter.cs:68-92`, `src/BlazorQL/BlazorQLIde.razor.cs:698`, `:1683`
  The document is never disposed on the success path (it cannot be, the returned `JsonElement`
  points into it), and this runs on every diagnostics pass and every run. Return
  `RootElement.Clone()` and dispose, or deserialize to a `JsonElement` directly.

- [ ] **Derived text recomputed per row per render**
  `src/BlazorQL/Components/TabBar.razor:27` (`TabStore.Title` runs a regex per tab),
  `src/BlazorQL/Components/History/HistoryPane.razor:54` (`DisplayText` splits the whole query per
  item), `src/BlazorQL/Services/NamedModels.cs:15-20` (an extra `GetModel` + `DisposeModel` round
  trip on every editor init), `src/BlazorQL/Fetchers/HttpFetcher.cs:74-87` (body read to a string,
  parsed, then cloned: three copies of a large response). All small on their own; cache the
  derived strings on the tab/item and parse the HTTP body from the stream.
