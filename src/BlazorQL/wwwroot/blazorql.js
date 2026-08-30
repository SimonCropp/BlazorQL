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

/// Builds the language-mode schema from an introspection result and returns the printed SDL —
/// the SDL view's content, produced by graphql-js so it is spec-correct for free.
export function setSchemaFromIntrospection(introspectionJson) {
    const parsed = JSON.parse(introspectionJson);
    const data = parsed.data ?? parsed;
    const schema = page.graphql.buildClientSchema(data);
    const sdl = page.graphql.printSchema(schema);
    setSchema(sdl);
    return sdl;
}

/// Wires the variables editor's JSON-Schema validation to the operation editor's declared
/// variables. The mode regenerates the JSON Schema in the worker on every operation change.
export function linkVariablesValidation(operationUriName, variablesUriName) {
    monacoGraphQL.setDiagnosticSettings({
        validateVariablesJSON: {
            [uriFor(operationUriName).toString()]: [uriFor(variablesUriName).toString()],
        },
        jsonDiagnosticSettings: {
            validate: true,
            schemaValidation: 'error',
            allowComments: true,
            trailingCommas: 'ignore',
        },
    });
}

/// The operations in a document and their variable types — what run-at-caret and the operation
/// picker read. Null when the document does not parse.
export function getOperationFacts(text) {
    try {
        const facts = page.languageService.getOperationASTFacts(page.graphql.parse(text));
        return JSON.stringify({
            operations: (facts.operations ?? []).map(op => ({
                name: op.name?.value ?? null,
                operation: op.operation,
                start: op.loc?.start ?? 0,
                end: op.loc?.end ?? 0,
            })),
        });
    } catch {
        return null;
    }
}

/// Parses JSONC (comments and trailing commas tolerated, as in GraphiQL's variables/headers
/// editors). Returns {ok, value?, error?}; a non-object root is refused like tryParseJSONC.
export function parseJsonc(text, what) {
    if (!text || text.trim().length === 0) {
        return JSON.stringify({ ok: true, value: null });
    }

    const errors = [];
    const value = page.jsoncParser.parse(text, errors, { allowTrailingComma: true });
    if (errors.length > 0) {
        return JSON.stringify({ ok: false, error: `${what} are invalid JSON: parse error at offset ${errors[0].offset}.` });
    }

    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
        return JSON.stringify({ ok: false, error: `${what} are not a JSON object.` });
    }

    return JSON.stringify({ ok: true, value });
}

// ---- Local (in-browser) schema execution, for the sample and any offline demo ----

let localSchema = null;
let localExecute = null;
// streamId -> async iterator (for cancellation)
const localStreams = new Map();

export async function initLocalSchema(moduleUrl) {
    // Resolved against the app's <base href>, so a sub-path host serves it unchanged.
    const module = await import(new URL(moduleUrl, document.baseURI).href);
    localSchema = module.createSchema(page.graphql);
    localExecute = module.createExecute
        ? module.createExecute(page.graphql)
        : page.graphql.execute;
}

export async function executeLocal(streamId, requestJson) {
    const request = JSON.parse(requestJson);
    try {
        const document = page.graphql.parse(request.query);
        const args = {
            schema: localSchema,
            document,
            variableValues: request.variables ?? undefined,
            operationName: request.operationName ?? undefined,
        };

        const operation = page.graphql.getOperationAST(document, request.operationName ?? undefined);
        const result = operation?.operation === 'subscription'
            ? await page.graphql.subscribe(args)
            : await localExecute(args);

        if (result != null && typeof result === 'object' && Symbol.asyncIterator in result) {
            const iterator = result[Symbol.asyncIterator]();
            localStreams.set(streamId, iterator);
            try {
                while (true) {
                    const next = await iterator.next();
                    if (next.done) {
                        break;
                    }

                    await dotNet.invokeMethodAsync('OnStreamNext', streamId, JSON.stringify(next.value));
                }

                await dotNet.invokeMethodAsync('OnStreamComplete', streamId);
            } finally {
                localStreams.delete(streamId);
            }

            return;
        }

        await dotNet.invokeMethodAsync('OnStreamNext', streamId, JSON.stringify(result));
        await dotNet.invokeMethodAsync('OnStreamComplete', streamId);
    } catch (error) {
        await dotNet.invokeMethodAsync('OnStreamError', streamId, String(error?.message ?? error));
    }
}

export function stopLocalStream(streamId) {
    const iterator = localStreams.get(streamId);
    localStreams.delete(streamId);
    iterator?.return?.();
}

export function focusEditor(uriName) {
    editors.get(uriName)?.editor.focus();
}

export function addAction(uriName, actionId, label, keybindingsJson) {
    const entry = editors.get(uriName);
    entry.listeners.push(entry.editor.addAction({
        id: actionId,
        label,
        contextMenuGroupId: 'graphql',
        keybindings: JSON.parse(keybindingsJson),
        run: () => dotNet.invokeMethodAsync('OnEditorAction', actionId),
    }));
}

export function getCursorOffset(uriName) {
    const entry = editors.get(uriName);
    const position = entry.editor.getPosition();
    return position ? entry.model.getOffsetAt(position) : 0;
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
