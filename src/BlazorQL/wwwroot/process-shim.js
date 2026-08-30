// graphql-js v17 reads globalThis.process.env.NODE_ENV; browsers and workers have neither.
globalThis.process ??= { env: {} };
globalThis.process.env ??= {};
