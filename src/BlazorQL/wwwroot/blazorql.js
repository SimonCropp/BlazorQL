// The single seam between C# and the vendored editor stack. One Monaco instance (the vendored
// monaco-graphql entry, published as globalThis.monaco), one graphql module per graph, workers
// self-hosted beside this file — all URLs relative to import.meta.url, so the app works unchanged
// under a sub-path host such as GitHub Pages.
import './process-shim.js';

let page = null;
let monaco = null;
let monacoGraphQL = null;
let dotNet = null;
let instancePrefix = '';

// uriName -> { editor, model, listeners: [] }
const editors = new Map();

function workerUrl(file) {
    return new URL(file, import.meta.url);
}

// GraphiQL's Monaco theme data, reduced: transparent editor background so the page's own
// background shows through, and accent colors aligned with the blazorql.css palette.
const themes = {
    'blazorql-light': {
        base: 'vs',
        inherit: true,
        rules: [{ token: 'argument.identifier.gql', foreground: '6c69ce' }],
        colors: {
            'editor.background': '#ffffff00',
            'scrollbar.shadow': '#ffffff00',
        },
    },
    'blazorql-dark': {
        base: 'vs-dark',
        inherit: true,
        rules: [{ token: 'argument.identifier.gql', foreground: '908aff' }],
        colors: {
            'editor.background': '#ffffff00',
            'scrollbar.shadow': '#ffffff00',
        },
    },
};

export async function init(dotNetRef, prefix) {
    dotNet = dotNetRef;
    instancePrefix = prefix ?? 'blazorql';

    globalThis.MonacoEnvironment = {
        getWorker(_workerId, label) {
            const file =
                label === 'graphql' ? './graphql.worker.js'
                : label === 'json' ? './json.worker.js'
                : './editor.worker.js';
            return new Worker(workerUrl(file), { type: 'module' });
        },
    };

    // The page bundle's own stylesheet (monaco widgets, codicons), injected here so the consuming
    // app links nothing vendor-specific.
    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.setAttribute('href', new URL('./vendor/page.css', import.meta.url).pathname);
    document.head.appendChild(link);

    page = await import('./vendor/page.js');
    monaco = page.monaco;
    globalThis.monaco = monaco;

    monaco.editor.defineTheme('blazorql-light', themes['blazorql-light']);
    monaco.editor.defineTheme('blazorql-dark', themes['blazorql-dark']);

    monacoGraphQL = page.initializeMode({
        diagnosticSettings: {
            jsonDiagnosticSettings: {
                validate: true,
                schemaValidation: 'error',
                // Comments and trailing commas are tolerated in variables/headers, as in GraphiQL.
                allowComments: true,
                trailingCommas: 'ignore',
            },
        },
    });

    return {
        userAgent: navigator.userAgent,
    };
}

function uriFor(uriName) {
    return monaco.Uri.file(`${instancePrefix}-${uriName}`);
}

// GraphiQL's shared editor construction defaults (create-editor.ts), verbatim where it matters:
// ligatures off works around a Windows caret-positioning bug; tabIndex -1 keeps editors out of the
// tab order (their wrappers are focusable instead).
const editorDefaults = {
    automaticLayout: true,
    fontSize: 15,
    tabSize: 2,
    fontFamily: '"Fira Code", monospace',
    fontLigatures: '"liga" off, "calt" off, "clig" off',
    minimap: { enabled: false },
    stickyScroll: { enabled: false },
    renderLineHighlight: 'none',
    overviewRulerLanes: 0,
    scrollBeyondLastLine: false,
    lineNumbersMinChars: 2,
    roundedSelection: false,
    scrollbar: { verticalScrollbarSize: 10 },
    tabIndex: -1,
};

export function createEditor(elementId, uriName, language, initialValue, overrides) {
    const element = document.getElementById(elementId);
    const model = monaco.editor.createModel(initialValue ?? '', language, uriFor(uriName));
    const editor = monaco.editor.create(element, {
        ...editorDefaults,
        ...(overrides ? JSON.parse(overrides) : {}),
        model,
    });
    editors.set(uriName, { editor, model, listeners: [] });
}

export function disposeEditor(uriName) {
    const entry = editors.get(uriName);
    if (!entry) {
        return;
    }

    for (const listener of entry.listeners) {
        listener.dispose();
    }

    entry.editor.dispose();
    entry.model.dispose();
    editors.delete(uriName);
}

export function getValue(uriName) {
    return editors.get(uriName)?.model.getValue() ?? '';
}

export function setValue(uriName, text) {
    editors.get(uriName)?.model.setValue(text ?? '');
}

export function onChange(uriName, debounceMs) {
    const entry = editors.get(uriName);
    let handle = null;
    const listener = entry.model.onDidChangeContent(() => {
        clearTimeout(handle);
        handle = setTimeout(() => {
            dotNet.invokeMethodAsync('OnEditorChanged', uriName, entry.model.getValue());
        }, debounceMs);
    });
    entry.listeners.push(listener);
}

/// Points the language mode at a schema, as SDL text — the one form that needs no graphql-js on
/// the main thread. Applies to every graphql model of this instance.
export function setSchema(sdl) {
    monacoGraphQL.setSchemaConfig([
        {
            uri: `${instancePrefix}-schema.graphql`,
            documentString: sdl,
            fileMatch: ['**'],
        },
    ]);
}

export function focusEditor(uriName) {
    editors.get(uriName)?.editor.focus();
}

export function updateEditorOptions(uriName, optionsJson) {
    editors.get(uriName)?.editor.updateOptions(JSON.parse(optionsJson));
}

export function setMonacoTheme(themeName) {
    monaco.editor.setTheme(themeName);
}

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
    for (const uriName of [...editors.keys()]) {
        disposeEditor(uriName);
    }

    dotNet = null;
}
