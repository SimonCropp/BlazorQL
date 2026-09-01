# Content Security Policy

The IDE is a WebAssembly application driving Monaco, so a policy written for ordinary
server-rendered pages will not run it. BlazorQL never writes a `Content-Security-Policy` header —
the policy belongs to the app — but an app that sets one has to widen it. This works:

```
default-src 'self';
script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval';
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
| `'unsafe-inline'`, or a nonce | The host page's inline bootstrap — the `require(...)` call that starts Blazor once Monaco has loaded. Without either the page renders its loading text and stops. |
| `font-src data:` | Monaco's icon font is a data uri inside its stylesheet. Without it the toolbar renders as empty boxes. |
| `worker-src blob:` | Monaco starts its language workers from a blob url. Without it the editors still work, but every keystroke logs a violation. |

`style-src 'unsafe-inline'` is what Monaco needs to write its own styles, and `connect-src` has to
name the graphql endpoint's origin when it is not the app's own — including the `ws://` or `wss://`
origin for subscriptions, which that directive also governs.

Nothing else is needed. The debug sidecar adds its stylesheet as a `<link>` to a same-origin file
rather than an inline `<style>`, so `style-src 'self'` already covers it.


## Dropping 'unsafe-inline'

The inline script is the one part of the policy worth removing, and where it lives depends on which
package you are using.

With the `BlazorQL` package you own `index.html`, so put a nonce on your own tags:

```html
<script nonce="@nonce">
    require(['vs/editor/editor.main'], () => Blazor.start(), () => Blazor.start());
</script>
```

With `BlazorQL.Bundled` the package renders the page, so it stamps the nonce for you — set the
`Nonce` option and the middleware puts it on every script element:

```csharp
app.Use((context, next) =>
{
    var nonce = RandomNumberGenerator.GetHexString(32);
    context.Items["CspNonce"] = nonce;
    context.Response.Headers.ContentSecurityPolicy =
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}' 'wasm-unsafe-eval'; ...";
    return next();
});

app.MapBlazorQL("/graphql-ide", _ => _.Nonce = context => (string?) context.Items["CspNonce"]);
```

Either way, keep `'self'` in `script-src`. Monaco's AMD loader injects further script elements at
runtime and those cannot carry a nonce, so a nonce-only policy needs `'strict-dynamic'` to reach
them.
