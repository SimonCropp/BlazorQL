// Starts Blazor by hand, once Monaco is actually there. Pair it with autostart="false" on
// blazor.webassembly.js and place it after Monaco's loader and editor.main.js.
//
// The editor.main.js tag only *registers* the module with the AMD loader; the loader then fetches
// the bundle's chunks and publishes window.monaco some time later. Blazor's own boot races that,
// and the editor components call monaco.editor.create the moment they first render — so whichever
// finishes second decides whether the page gets editors at all. Requiring the module before
// starting Blazor removes the race in both directions; the errorback still starts the app if
// Monaco genuinely fails to load.
//
// A file rather than an inline script so that no part of the page needs 'unsafe-inline' or a
// nonce: script-src 'self' covers it. See docs/csp.md.
require(['vs/editor/editor.main'], () => Blazor.start(), () => Blazor.start());
