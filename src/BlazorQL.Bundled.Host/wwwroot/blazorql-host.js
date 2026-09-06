// The one bridge between the injected configuration and the app. The middleware writes that
// configuration into a <script type="application/json"> data block — a type the browser never
// executes, and so never checks against script-src, which is what keeps the page free of both
// 'unsafe-inline' and a nonce.
//
// Reading through a function rather than at load means a page served without the injection - a
// stale cache, a proxy that stripped the element - starts on defaults instead of throwing at first
// render. JSON.parse, so no eval either.

let config = null;

function read() {
    const element = document.getElementById('blazorql-config');
    if (!element) {
        return {};
    }

    try {
        return JSON.parse(element.textContent) ?? {};
    } catch {
        return {};
    }
}

window.blazorqlHost = {
    config: () => config ??= read(),
    origin: () => location.origin
};
