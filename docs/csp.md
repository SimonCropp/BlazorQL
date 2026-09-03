# Content Security Policy

The IDE is a WebAssembly application driving Monaco, so a policy written for ordinary
server-rendered pages will not run it. Which directives it needs is the package's business rather
than the app's, so `BlazorQL.Bundled` will send them:

```csharp
app.MapBlazorQL("/graphql-ide", _ => _.WriteContentSecurityPolicy = true);
```

That writes the policy on the page the mount serves, mints a per-request nonce, and stamps that
nonce on every script element — no middleware, and nothing to keep in step by hand. It is off by
default, because a policy is the app's to decide.

Add the app's own directives, or widen one the IDE leaves narrow, through
`ConfigureContentSecurityPolicy`. They are mutable and keyed by directive name, so an entry can be
replaced — appending a duplicate to the header would change nothing, because the first occurrence
of a directive is the one that counts:

```csharp
app.MapBlazorQL(
    "/graphql-ide",
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
script-src 'self' 'nonce-…' 'wasm-unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data:;
font-src 'self' data:;
connect-src 'self';
worker-src 'self' blob:
```

The same policy covers both packages: what needs widening comes from Blazor WebAssembly and Monaco,
not from how the IDE is delivered.

Four of those are not obvious, and each fails in its own way:

| | |
| --- | --- |
| `'wasm-unsafe-eval'` | Compiling the .NET runtime. Without it the app never starts. |
| a nonce, or `'unsafe-inline'` | The host page's inline bootstrap — the `require(...)` call that starts Blazor once Monaco has loaded. Without either the page renders its loading text and stops. |
| `font-src data:` | Monaco's icon font is a data uri inside its stylesheet. Without it the toolbar renders as empty boxes. |
| `worker-src blob:` | Monaco starts its language workers from a blob url. Without it the editors still work, but every keystroke logs a violation. |

`style-src 'unsafe-inline'` is what Monaco needs to write its own styles, and `connect-src` has to
name the graphql endpoint's origin when it is not the app's own — including the `ws://` or `wss://`
origin for subscriptions, which that directive also governs.

Nothing else is needed. The debug sidecar adds its stylesheet as a `<link>` to a same-origin file
rather than an inline `<style>`, so `style-src 'self'` already covers it.


## Writing the header elsewhere

An app that builds one policy for every route can still take the directives from the package rather
than transcribing them. `ContentSecurityPolicy.Build` returns the header value and
`ContentSecurityPolicy.Directives` returns the map behind it, both taking the same `configure`
shape:

```csharp
var policy = ContentSecurityPolicy.Build(nonce, _ => _["frame-ancestors"] = "'none'");
```

Pair it with the `Nonce` option, which hands the mount a nonce the app has already minted instead
of generating one:

```csharp
app.MapBlazorQL("/graphql-ide", _ => _.Nonce = context => (string?) context.Items["CspNonce"]);
```


## The BlazorQL package

The RCL leaves `index.html` to the consuming app, so there is no page for it to stamp — the nonce
goes on the app's own tag:

```html
<script nonce="@nonce">
    require(['vs/editor/editor.main'], () => Blazor.start(), () => Blazor.start());
</script>
```

Either way, keep `'self'` in `script-src`. Monaco's AMD loader injects further script elements at
runtime and those cannot carry a nonce, so a nonce-only policy needs `'strict-dynamic'` to reach
them.
