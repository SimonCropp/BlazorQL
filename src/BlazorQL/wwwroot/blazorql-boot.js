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
// The loading screen also says which build it is (window.blazorqlVersion, from js/version.js when
// the page loads it) and why it stopped if the app cannot start: a boot failure is otherwise only in
// the console, which a phone does not show, and the loading text stays up. Any index.html works: the
// script looks for #app .loading and adds the elements it writes to; a page without one only boots.
// Once the app has rendered, the loading screen is gone and nothing more is written.
//
// A file rather than an inline script so that no part of the page needs 'unsafe-inline' or a
// nonce: script-src 'self' covers it. See docs/csp.md.
(() => {
    const loading = document.querySelector('#app .loading');

    const part = name => {
        let element = loading.querySelector(`.${name}`);
        if (!element) {
            element = document.createElement('p');
            element.className = name;
            loading.append(element);
        }

        return element;
    };

    if (loading) {
        // A bare <div class="loading">Loading…</div> becomes the message, so that a failure
        // replaces the text rather than sitting under it.
        if (!loading.querySelector('.loading-message')) {
            const message = document.createElement('p');
            message.className = 'loading-message';
            message.append(...loading.childNodes);
            loading.append(message);
        }

        part('loading-version').textContent = window.blazorqlVersion ?? '';
    }

    const fail = reason => {
        if (!loading?.isConnected) {
            return;
        }

        loading.classList.add('failed');
        part('loading-message').textContent = 'BlazorQL could not start';
        const error = part('loading-error');
        error.textContent = String(reason?.message ?? reason ?? 'Unknown error');
        error.hidden = false;
    };
    addEventListener('error', event => fail(event.error ?? event.message));
    addEventListener('unhandledrejection', event => fail(event.reason));

    if (typeof WebAssembly !== 'object') {
        fail('This browser has WebAssembly turned off, and BlazorQL runs on it. On an iPhone or iPad, Lockdown Mode turns it off.');
        return;
    }

    require(
        ['vs/editor/editor.main'],
        () => Blazor.start().catch(fail),
        () => Blazor.start().catch(fail));
})();
