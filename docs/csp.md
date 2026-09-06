# Content Security Policy

The IDE is a WebAssembly application driving Monaco, so a policy written for ordinary
server-rendered pages will not run it. Which directives it needs is the package's business rather
than the app's, so `BlazorQL.Bundled` will send them:

```csharp
app.MapBlazorQL("/blazorql", _ => _.WriteContentSecurityPolicy = true);
```

That writes the policy on the page the mount serves — no middleware, and nothing to keep in step by
hand. It is off by default, because a policy is the app's to decide.

The page carries no inline script, so the policy needs neither `'unsafe-inline'` nor a nonce: the
bootstrap is a file, and the mount's configuration travels in a `<script type="application/json">`
data block, which is a type the browser never executes and so never checks against `script-src`.
The header is the same bytes on every request.

Add the app's own directives, or widen one the IDE leaves narrow, through
`ConfigureContentSecurityPolicy`. They are mutable and keyed by directive name, so an entry can be
replaced — appending a duplicate to the header would change nothing, because the first occurrence
of a directive is the one that counts:

```csharp
app.MapBlazorQL(
    "/blazorql",
    _ =>
    {
        _.WriteContentSecurityPolicy = true;
        _.ConfigureContentSecurityPolicy = _ =>
        {
            _["connect-src"] = "'self' https://api.example.com";
            _["frame-ancestors"] = "'none'";
        };
    });
```

A response that already carries a policy keeps it: an app that writes its own for the mount means
it, and two policies intersect rather than the second replacing the first.


## The policy

What the option sends, and what an app composing its own has to include:

```
default-src 'self';
script-src 'self' 'wasm-unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data:;
font-src 'self' data:;
connect-src 'self';
worker-src 'self' blob:
```

The same policy covers both packages: what needs widening comes from Blazor WebAssembly and Monaco,
not from how the IDE is delivered.

Three of those are not obvious, and each fails silently in its own way:

| | |
| --- | --- |
| `'wasm-unsafe-eval'` | Compiling the .NET runtime. Without it the app never starts. |
| `font-src data:` | Monaco's icon font is a data uri inside its stylesheet. Without it the toolbar renders as empty boxes. |
| `worker-src blob:` | Monaco starts its language workers from a blob url. Without it the editors still work, but every keystroke logs a violation. |

`style-src 'unsafe-inline'` is what Monaco needs to write its own styles, and `connect-src` has to
name the graphql endpoint's origin when it is not the app's own — including the `ws://` or `wss://`
origin for subscriptions, which that directive also governs.

`script-src` needs `'self'` and nothing more. Monaco's AMD loader injects further script elements at
runtime, and those are same-origin files like every other script on the page.

Nothing else is needed. The debug sidecar adds its stylesheet as a `<link>` to a same-origin file
rather than an inline `<style>`, so `style-src 'self'` already covers it.


## Writing the header elsewhere

An app that builds one policy for every route can still take the directives from the package rather
than transcribing them. `ContentSecurityPolicy.Build` returns the header value and
`ContentSecurityPolicy.Directives` returns the map behind it, both taking the same `configure`
shape:

```csharp
var policy = ContentSecurityPolicy.Build(configure: _ => _["frame-ancestors"] = "'none'");
```

Because the policy holds nothing per-request, it can be built once at startup and set from
middleware, or written into a static `web.config`, `_headers` file or CDN rule.


## Nonce-based policies

An app whose site-wide policy names a nonce and no host source — `script-src 'nonce-…'
'strict-dynamic'` — can hand that nonce to the mount, which stamps it onto every script element in
the page:

```csharp
app.MapBlazorQL("/blazorql", _ => _.Nonce = context => (string?) context.Items["CspNonce"]);
```

`Build` takes the same value, so the header and the page agree:

```csharp
var policy = ContentSecurityPolicy.Build(nonce);
```

This is interoperability, not hardening: the IDE has nothing a nonce protects that `'self'` does
not. And a nonce-only policy needs `'strict-dynamic'`, because the script elements Monaco's AMD
loader injects at runtime cannot carry one.


## The BlazorQL package

The RCL leaves `index.html` to the consuming app. Load the boot script from the package's static
assets rather than inlining the call, and the app's page needs no nonce either — which is what lets
a statically hosted WebAssembly app, where there is no server to mint one per request, run under
this policy at all:

```html
<script src="_framework/blazor.webassembly.js" autostart="false"></script>
<script src="_content/BlazorQL/blazorql-boot.js"></script>
```

See [Getting started](getting-started.md) for the full set of tags and the order they go in.
