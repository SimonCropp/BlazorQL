// The one bridge between the injected configuration and the app. Reading through a function rather
// than the global directly means a page served without the injection - a stale cache, a proxy that
// stripped the script - starts on defaults instead of throwing at first render. No eval, so a host
// with a strict content-security-policy is unaffected.
window.blazorqlHost = {
    config: () => window.blazorqlConfig ?? {},
    origin: () => location.origin
};
