// The single seam between C# and the vendored editor stack. One Monaco instance (the vendored
// monaco-graphql entry, published as globalThis.monaco), one graphql module per graph, workers
// self-hosted beside this file — all URLs relative to import.meta.url, so the app works unchanged
// under a sub-path host such as GitHub Pages.
import './process-shim.js';
// Ported GraphiQL helpers. Factories rather than modules: graphql-js relies on instanceof, so
// they are bound to the page bundle's own graphql instance during init.
import { createMergeAst } from './ported/merge-ast.js';
import { createFillLeafs } from './ported/fill-leafs.js';

let page = null;
let monaco = null;
let monacoGraphQL = null;
let dotNet = null;
let instancePrefix = '';
let mergeAst = null;
let runFillLeafs = null;
// The lazily imported prettier bundle — a separate vendored file, loaded on first prettify.
let prettier = null;

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
    mergeAst = createMergeAst(page.graphql);
    runFillLeafs = createFillLeafs(page.graphql);

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

// The graphql-js schema built from the last introspection — what jump-to-doc resolves against.
let pageSchema = null;

/// Builds the language-mode schema from an introspection result and returns the printed SDL —
/// the SDL view's content, produced by graphql-js so it is spec-correct for free.
export function setSchemaFromIntrospection(introspectionJson) {
    const parsed = JSON.parse(introspectionJson);
    const data = parsed.data ?? parsed;
    const schema = page.graphql.buildClientSchema(data);
    pageSchema = schema;
    const sdl = page.graphql.printSchema(schema);
    setSchema(sdl);
    return sdl;
}

/// Ctrl/Cmd+click jump-to-doc on the operation editor. The language service resolves the token
/// under the pointer against the schema; a resolvable field, argument, or type reference goes to
/// C# as a flattened {kind, typeName, fieldName?, argName?} through the callback hub. A mouse
/// gesture rather than a Monaco DefinitionProvider: Monaco also invokes definition providers
/// during ctrl-hover link detection, which would open the docs on hover.
export function registerJumpToDoc(operationUriName) {
    const entry = editors.get(operationUriName);
    entry.listeners.push(entry.editor.onMouseDown(mouseEvent => {
        const pointer = mouseEvent.event;
        if (!(pointer.ctrlKey || pointer.metaKey) || pointer.rightButton) {
            return;
        }

        const position = mouseEvent.target.position;
        if (!position || !pageSchema) {
            return;
        }

        const reference = schemaReferenceAt(entry.model.getValue(), position);
        if (reference) {
            pointer.preventDefault();
            dotNet.invokeMethodAsync('OnSchemaReference', JSON.stringify(reference));
        }
    }));
}

function schemaReferenceAt(text, position) {
    let context = null;
    try {
        context = page.languageService.getContextAtPosition(
            text,
            // Monaco positions are 1-based; the language service's are 0-based.
            { line: position.lineNumber - 1, character: position.column - 1 },
            pageSchema);
    } catch {
        return null;
    }

    if (!context) {
        return null;
    }

    const named = type => (type ? (page.graphql.getNamedType(type)?.name ?? null) : null);
    const info = context.typeInfo;
    switch (context.state.kind) {
        case 'Field':
        case 'AliasedField':
            if (info.fieldDef && info.parentType) {
                return { kind: 'Field', typeName: named(info.parentType), fieldName: info.fieldDef.name };
            }
            break;
        case 'Argument':
            if (info.argDef && info.fieldDef && info.parentType) {
                return {
                    kind: 'Argument',
                    typeName: named(info.parentType),
                    fieldName: info.fieldDef.name,
                    argName: info.argDef.name,
                };
            }
            break;
        case 'NamedType':
            if (info.type) {
                return { kind: 'Type', typeName: named(info.type) };
            }
            break;
    }

    return null;
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

// ---- Toolbar operations (prettify, merge, copy) and fill-leaves ----

/// Reformats the editor's content — prettier's GraphQL formatter for graphql models, its JSONC
/// formatter for the rest. A parse failure is the user's problem to fix, not an exception:
/// swallowed with a warning, and never thrown to C#.
export async function prettify(uriName) {
    const entry = editors.get(uriName);
    if (!entry) {
        return;
    }

    const text = entry.model.getValue();
    if (text.trim().length === 0) {
        return;
    }

    try {
        prettier ??= await import(new URL('./vendor/prettier.js', import.meta.url).href);
        const formatted = entry.model.getLanguageId() === 'graphql'
            ? await prettier.formatGraphQL(text)
            : await prettier.formatJsonc(text);
        if (formatted !== text) {
            entry.model.setValue(formatted);
        }
    } catch (error) {
        console.warn(`prettify(${uriName}) skipped:`, error?.message ?? error);
    }
}

/// Inlines every named fragment into the operations, GraphiQL-style. Returns {ok, error?}; a
/// document that does not parse reports its error for C# to surface in the response pane.
export function mergeFragments(uriName) {
    const entry = editors.get(uriName);
    const text = entry.model.getValue();
    try {
        const merged = page.graphql.print(mergeAst(page.graphql.parse(text), pageSchema ?? undefined));
        if (merged !== text) {
            entry.model.setValue(merged);
        }

        return JSON.stringify({ ok: true });
    } catch (error) {
        return JSON.stringify({ ok: false, error: String(error?.message ?? error) });
    }
}

/// Best-effort clipboard write — denied permission or an insecure context degrade to a no-op.
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
    } catch {
        // Clipboard unavailable; nothing useful to do.
    }
}

/// Fills in default leaf selections for fields that need them, returning the text the run should
/// send. Inserted ranges are highlighted for a few seconds so the user sees what was added.
/// Simplification vs GraphiQL: the cursor keeps its position rather than being remapped through
/// the insertion offsets.
export function fillLeafs(uriName) {
    const entry = editors.get(uriName);
    const text = entry.model.getValue();
    if (!pageSchema) {
        return text;
    }

    try {
        const { insertions, result } = runFillLeafs(pageSchema, text);
        if (insertions.length === 0) {
            return text;
        }

        const position = entry.editor.getPosition();
        entry.model.setValue(result);
        if (position) {
            entry.editor.setPosition(position);
        }

        decorateInsertions(entry, insertions);
        return result;
    } catch (error) {
        console.warn(`fillLeafs(${uriName}) skipped:`, error?.message ?? error);
        return text;
    }
}

function decorateInsertions(entry, insertions) {
    // Insertion indices address the original text; earlier insertions shift the later ones.
    let shift = 0;
    const decorations = [];
    for (const { index, string } of insertions) {
        const start = entry.model.getPositionAt(index + shift);
        const end = entry.model.getPositionAt(index + shift + string.length);
        decorations.push({
            range: new monaco.Range(start.lineNumber, start.column, end.lineNumber, end.column),
            options: {
                className: 'blazorql-auto-inserted-leaf',
                hoverMessage: { value: 'Automatically added leaf fields' },
            },
        });
        shift += string.length;
    }

    const collection = entry.editor.createDecorationsCollection(decorations);
    setTimeout(() => collection.clear(), 7000);
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

// ---- Share links, downloads, response image hover ----

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

const imageToken = /\S+\.(png|svg|jpe?g|gif|webp)$/i;
// Hover providers register per-language; disposed with the module.
const languageProviders = [];

/// Hovering a value ending in an image extension in the response editor previews the image, via a
/// markdown image in the hover.
export function registerResponseImageHover(uriName) {
    const target = uriFor(uriName).toString();
    languageProviders.push(monaco.languages.registerHoverProvider('json', {
        provideHover(model, position) {
            if (model.uri.toString() !== target) {
                return null;
            }

            const line = model.getLineContent(position.lineNumber);
            const isBoundary = ch => ch === '"' || ch === ' ' || ch === '\t' || ch === ',';
            let start = position.column - 1;
            let end = start;
            while (start > 0 && !isBoundary(line[start - 1])) {
                start--;
            }

            while (end < line.length && !isBoundary(line[end])) {
                end++;
            }

            const token = line.slice(start, end);
            if (!imageToken.test(token)) {
                return null;
            }

            return {
                range: new monaco.Range(position.lineNumber, start + 1, position.lineNumber, end + 1),
                contents: [{ value: `![](${token})` }],
            };
        },
    }));
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

    for (const detach of pointerTrackers.splice(0)) {
        detach();
    }

    if (shortcutListener) {
        document.removeEventListener('keydown', shortcutListener);
        shortcutListener = null;
    }

    for (const provider of languageProviders.splice(0)) {
        provider.dispose();
    }

    dotNet = null;
}
