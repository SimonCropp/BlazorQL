// The page-side bundle entry. Everything Monaco-flavored the page runs must live in ONE bundle —
// separately bundled monaco files each carry their own language registry, and an editor from one
// registry never sees languages registered into another. esbuild resolves and dedupes the whole
// graph here, so the page has exactly one Monaco and exactly one graphql.
import 'monaco-editor/esm/vs/basic-languages/graphql/graphql.contribution.js';
import 'monaco-editor/esm/vs/language/json/monaco.contribution.js';

export * as monaco from 'monaco-editor/esm/vs/editor/edcore.main.js';
export { initializeMode } from 'monaco-graphql/esm/initializeMode.js';
export * as graphql from 'graphql';
export * as languageService from 'graphql-language-service';
export * as jsoncParser from 'jsonc-parser';
