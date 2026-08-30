/**
 * Ported from GraphiQL's @graphiql/toolkit operation-name.ts (type annotations stripped).
 * Copyright (c) GraphQL Contributors. MIT licensed.
 * https://github.com/graphql/graphiql/blob/main/packages/graphiql-toolkit/src/graphql-helpers/operation-name.ts
 */

/**
 * Provided optional previous operations and selected name, and a next list of
 * operations, determine what the next selected operation should be.
 */
export function getSelectedOperationName(
  prevOperations,
  prevSelectedOperationName,
  operations,
) {
  // If there are not enough operations to bother with, return nothing.
  if (!operations || operations.length < 1) {
    return;
  }

  // If a previous selection still exists, continue to use it.
  const names = operations.map(op => op.name?.value);
  if (prevSelectedOperationName && names.includes(prevSelectedOperationName)) {
    return prevSelectedOperationName;
  }

  // If a previous selection was the Nth operation, use the same Nth.
  if (prevSelectedOperationName && prevOperations) {
    const prevNames = prevOperations.map(op => op.name?.value);
    const prevIndex = prevNames.indexOf(prevSelectedOperationName);
    if (prevIndex !== -1 && prevIndex < names.length) {
      return names[prevIndex];
    }
  }

  // Use the first operation.
  return names[0];
}
