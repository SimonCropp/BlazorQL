// Glue for the vendored GraphiQL test schema: one module in the shape BlazorQL's local fetcher
// loads. Both files are copied verbatim from graphql/graphiql (MIT, GraphQL Contributors).
export { createSchema } from './schema.js';
export { createExecute } from './execute.js';
