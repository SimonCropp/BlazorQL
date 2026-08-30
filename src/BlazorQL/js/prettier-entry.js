// Prettier, lazily imported by the host module only when a prettify action runs.
import * as prettier from 'prettier/standalone';
import * as graphqlPlugin from 'prettier/plugins/graphql';
import * as estreePlugin from 'prettier/plugins/estree';
import * as babelPlugin from 'prettier/plugins/babel';

export function formatGraphQL(text) {
    return prettier.format(text, {
        parser: 'graphql',
        plugins: [graphqlPlugin],
    });
}

// printWidth 0 keeps one key per line, matching GraphiQL's variables/headers formatting.
export function formatJsonc(text) {
    return prettier.format(text, {
        parser: 'jsonc',
        plugins: [estreePlugin, babelPlugin],
        printWidth: 0,
    });
}
