# BlazorQL.Bundled

An in-browser GraphQL IDE for an ASP.NET Core app that is not a Blazor app, in one assembly.

The `BlazorQL` package is a Razor Class Library: it assumes a Blazor WebAssembly host, and it needs
five script and stylesheet tags wired into `index.html` in a load-bearing order. That is a
reasonable ask of a team already running Blazor, and an unreasonable one of a team that has a
GraphQL endpoint and wants an IDE next to it and nothing more.

`BlazorQL.Bundled` covers that second case. One package reference, one line of code:

```
dotnet add package BlazorQL.Bundled
```

```csharp
app.MapBlazorQL("/graphql-ide", _ => _.Endpoint = "/graphql");
```

Nothing else is deployed. The whole WebAssembly application — the .NET runtime, the IDE, and the
Monaco editor — is embedded in the assembly and served from a routed endpoint. No Blazor SDK, no
static files, no `index.html`, and **no package dependencies at all**.


## What it costs

The assembly is about 3.4 MB, because it contains an entire application. That is what a browser
downloads across a first visit, and it is cached from then on. For comparison,
`Swashbuckle.AspNetCore.SwaggerUI` is around 3 MB.

Everything is stored brotli-compressed and served that way, so the bytes on the wire are the bytes
in the dll. A client that will not accept brotli gets the file decompressed on demand.


## Options

Everything is configured through `MapBlazorQL`:

| Option | Default | |
| --- | --- | --- |
| `Endpoint` | `/graphql` | Where queries and mutations are posted. Root-relative or absolute; a `ws://` or `wss://` url runs the whole session over graphql-transport-ws. |
| `SubscriptionEndpoint` | null | A separate websocket endpoint for subscriptions, the way GraphiQL pairs `url` with `subscriptionUrl`. Queries and mutations keep using `Endpoint`. |
| `DefaultQuery` | null | The query a fresh tab starts with. |
| `DefaultHeaders` | null | The request headers a fresh tab starts with, as a JSON object. |
| `IsHeadersEditorEnabled` | true | |
| `ShouldPersistHeaders` | false | Whether headers survive a reload. Off by default, because headers usually hold credentials. |
| `MaxHistoryLength` | 20 | |
| `StorageNamespace` | `blazorql` | Namespaces the local-storage keys. Changing it isolates two mounts from each other. |
| `DefaultTheme` / `ForcedTheme` | System / null | `System`, `Light` or `Dark`. A forced theme hides the setting. |
| `DocumentTitle` | `GraphQL IDE` | The browser tab title. |
| `BasePathOverride` | null | Overrides the base path baked into the page. See *Behind a proxy*. |
| `MapUnknownPathsToIde` | false | Serves the IDE for unknown extensionless paths under the mount instead of answering 404. |
| `WriteContentSecurityPolicy` | false | Sends the policy the IDE needs on the page, with a per-request nonce. See *Content Security Policy*. |
| `ConfigureContentSecurityPolicy` | null | Adds to or replaces those directives. |
| `Nonce` | null | `Func<HttpContext, string?>` supplying the CSP nonce to stamp on every script element. See *Content Security Policy*. |

The endpoint is resolved in the browser against the page it was served from, so a root-relative
`/graphql` follows the app wherever it is hosted.


## Authorization

`MapBlazorQL` returns the endpoints it registered, so conventions apply to both of them:

```csharp
app.MapBlazorQL("/graphql-ide").RequireAuthorization("Admin");
```

This works with cookie authentication. It does **not** work with a bearer-only scheme: the
WebAssembly application's own asset requests are ordinary browser fetches and carry no bearer token,
so they would be rejected with 401.


## Content Security Policy

The IDE is a WebAssembly application, so a `'self'` policy stops it dead: no runtime, no icons, no
language workers. Rather than restate the directives in every app, the mount can send them:

```csharp
app.MapBlazorQL("/graphql-ide", _ => _.WriteContentSecurityPolicy = true);
```

That also mints a per-request nonce and stamps it on every script element in the page, so the
policy names a nonce rather than allowing `'unsafe-inline'`. A response that already carries a
policy keeps it.

[The whole policy, what each directive is for, and how to fold it into a header the app writes
itself](csp.md).


## Behind a proxy

The page is served with a `<base href>` built from the request's path base plus the mount, so
`UsePathBase` and IIS virtual directories compose without configuration.

A reverse proxy that strips a prefix without sending `X-Forwarded-Prefix` breaks that, because
nothing downstream can know the public path. `UseForwardedHeaders` with
`ForwardedHeaders.XForwardedPrefix` is the fix; `BasePathOverride` is the fallback when the proxy
cannot be changed.

A proxy that rewrites response bodies — HTML injection, script-nonce rewriting, or decompressing and
recompressing — breaks the runtime's integrity checks, and the app then fails to boot with an
integrity error rather than anything more helpful.


## Response compression

Assets are written with `Content-Encoding: br` already set, and ASP.NET Core's response compression
middleware skips any response that already carries one. Enabling `UseResponseCompression()` globally
is therefore safe, and a test covers it.


## Security

Every option is serialized into the page, so anything in `DefaultHeaders` is visible to anyone who
can reach the endpoint. An API key does not belong there.


## Relationship to the BlazorQL package

Same IDE, different delivery. `BlazorQL` suits an app that is already Blazor and wants the
`<BlazorQLIde/>` component placed in its own layout, with its own fetcher, logo and toolbar content.
`BlazorQL.Bundled` suits an app that wants the IDE at a url and no further involvement.

The bundled package does not depend on `BlazorQL`; it embeds a build of it.
