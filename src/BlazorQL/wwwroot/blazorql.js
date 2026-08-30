// Pure browser utilities behind one module seam. The editors themselves are BlazorMonaco
// components and every language feature runs in C# — nothing here touches Monaco.

let dotNet = null;

export function init(dotNetRef) {
    dotNet = dotNetRef;
}

// ---- Global shortcuts ----

let shortcutListener = null;

/// Document-level shortcuts for commands that live outside any editor. Entries are
/// {id, key, ctrl, shift, alt, meta}, matched on event.key case-insensitively with an exact
/// modifier match; a match is consumed (preventDefault) and routed to the callback hub.
export function registerGlobalShortcuts(jsonArray) {
    const shortcuts = JSON.parse(jsonArray);
    shortcutListener = event => {
        for (const shortcut of shortcuts) {
            if (event.key?.toLowerCase() === shortcut.key.toLowerCase() &&
                event.ctrlKey === shortcut.ctrl &&
                event.shiftKey === shortcut.shift &&
                event.altKey === shortcut.alt &&
                event.metaKey === shortcut.meta) {
                event.preventDefault();
                dotNet.invokeMethodAsync('OnGlobalShortcut', shortcut.id);
                return;
            }
        }
    };
    document.addEventListener('keydown', shortcutListener);
}

export function focusElement(selector) {
    document.querySelector(selector)?.focus();
}

// ---- Share links, downloads, clipboard ----

export function getHash() {
    return location.hash;
}

/// Replaces the location's fragment (no history entry) and returns the resulting href — what the
/// share button copies.
export function setHash(fragment) {
    history.replaceState(null, '', location.pathname + location.search + '#' + fragment);
    return location.href;
}

export function downloadText(name, text, mimeType) {
    const url = URL.createObjectURL(new Blob([text], { type: mimeType }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.click();
    URL.revokeObjectURL(url);
}

/// Best-effort clipboard write — denied permission or an insecure context degrade to a no-op.
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
    } catch {
        // Clipboard unavailable; nothing useful to do.
    }
}

// ---- localStorage, behind the module seam so C# owns namespacing and policy ----

export function storageGet(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

/// Returns {ok, error?} as JSON: quota (and privacy-mode) failures surface as a result rather than
/// an interop exception, so C# can report them as a boolean.
export function storageSet(key, value) {
    try {
        localStorage.setItem(key, value);
        return JSON.stringify({ ok: true });
    } catch (error) {
        return JSON.stringify({ ok: false, error: String(error?.message ?? error) });
    }
}

export function storageRemove(key) {
    try {
        localStorage.removeItem(key);
    } catch {
        // Nothing to remove when storage itself is unavailable.
    }
}

export function storageKeys(prefix) {
    try {
        return Object.keys(localStorage).filter(key => key.startsWith(prefix));
    } catch {
        return [];
    }
}

// ---- Pane resizing ----

// Detachers for the pane-resizer pointerdown listeners, run on dispose.
const pointerTrackers = [];

/// Attaches pane-resize dragging to a drag-bar element. While a pointer is captured, every move
/// reports the pointer's fractional position within the bar's parent container (and the
/// container's size on that axis, so C# can apply pixel thresholds) through the callback hub.
export function trackPointer(elementId, resizerId, direction) {
    const element = document.getElementById(elementId);
    const onDown = down => {
        down.preventDefault();
        element.setPointerCapture(down.pointerId);
        const onMove = move => {
            const rect = element.parentElement.getBoundingClientRect();
            const size = direction === 'x' ? rect.width : rect.height;
            const offset = direction === 'x' ? move.clientX - rect.left : move.clientY - rect.top;
            if (size > 0) {
                dotNet.invokeMethodAsync('OnPaneResize', resizerId, Math.min(Math.max(offset / size, 0), 1), size);
            }
        };
        const stop = up => {
            element.releasePointerCapture(up.pointerId);
            element.removeEventListener('pointermove', onMove);
            element.removeEventListener('pointerup', stop);
            element.removeEventListener('pointercancel', stop);
        };
        element.addEventListener('pointermove', onMove);
        element.addEventListener('pointerup', stop);
        element.addEventListener('pointercancel', stop);
    };
    element.addEventListener('pointerdown', onDown);
    pointerTrackers.push(() => element.removeEventListener('pointerdown', onDown));
}

// ---- Theme ----

export function setDataTheme(mode) {
    document.documentElement.dataset.theme = mode;
}

export function systemDark() {
    try {
        return window.matchMedia('(prefers-color-scheme: dark)').matches;
    } catch {
        return false;
    }
}

export function dispose() {
    for (const detach of pointerTrackers.splice(0)) {
        detach();
    }

    if (shortcutListener) {
        document.removeEventListener('keydown', shortcutListener);
        shortcutListener = null;
    }

    dotNet = null;
}
