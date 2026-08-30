/**
 * Ported from GraphiQL's @graphiql/toolkit auto-complete.ts (type annotations stripped).
 * Copyright (c) GraphQL Contributors. MIT licensed.
 * https://github.com/graphql/graphiql/blob/main/packages/graphiql-toolkit/src/graphql-helpers/auto-complete.ts
 */
import {
  getNamedType,
  isLeafType,
  Kind,
  parse,
  print,
  TypeInfo,
  visit,
} from '../vendor/graphql.js';

/**
 * Given a document string which may not be valid due to terminal fields not
 * representing leaf values (Spec Section: "Leaf Field Selections"), and a
 * function which provides reasonable default field names for a given type,
 * this function will attempt to produce a schema which is valid after filling
 * in selection sets for the invalid fields.
 *
 * Note that there is no guarantee that the result will be a valid query, this
 * utility represents a "best effort" which may be useful within IDE tools.
 */
export function fillLeafs(schema, docString, getDefaultFieldNames) {
  const insertions = [];

  if (!schema || !docString) {
    return { insertions, result: docString };
  }

  let ast;
  try {
    ast = parse(docString);
  } catch {
    return { insertions, result: docString };
  }

  const fieldNameFn = getDefaultFieldNames || defaultGetDefaultFieldNames;
  const typeInfo = new TypeInfo(schema);
  visit(ast, {
    leave(node) {
      typeInfo.leave(node);
    },
    enter(node) {
      typeInfo.enter(node);
      if (node.kind === 'Field' && !node.selectionSet) {
        const fieldType = typeInfo.getType();
        const selectionSet = buildSelectionSet(fieldType, fieldNameFn);
        if (selectionSet && node.loc) {
          const indent = getIndentation(docString, node.loc.start);
          insertions.push({
            index: node.loc.end,
            string: ' ' + print(selectionSet).replaceAll('\n', '\n' + indent),
          });
        }
      }
    },
  });

  // Apply the insertions, but also return the insertions metadata.
  return {
    insertions,
    result: withInsertions(docString, insertions),
  };
}

// The default function to use for producing the default fields from a type.
// This function first looks for some common patterns, and falls back to
// including all leaf-type fields.
function defaultGetDefaultFieldNames(type) {
  // If this type cannot access fields, then return an empty set.
  if (!('getFields' in type)) {
    return [];
  }

  const fields = type.getFields();

  // Is there an `id` field?
  if (fields.id) {
    return ['id'];
  }

  // Is there an `edges` field?
  if (fields.edges) {
    return ['edges'];
  }

  // Is there an `node` field?
  if (fields.node) {
    return ['node'];
  }

  // Include all leaf-type fields.
  const leafFieldNames = [];
  for (const fieldName of Object.keys(fields)) {
    if (isLeafType(fields[fieldName].type)) {
      leafFieldNames.push(fieldName);
    }
  }
  return leafFieldNames;
}

// Given a GraphQL type, and a function which produces field names, recursively
// generate a SelectionSet which includes default fields.
function buildSelectionSet(type, getDefaultFieldNames) {
  // Unknown types and leaf types do not have selection sets.
  if (!type || isLeafType(type)) {
    return;
  }

  // Unwrap any non-null or list types.
  const namedType = getNamedType(type);

  // Get an array of field names to use.
  const fieldNames = getDefaultFieldNames(namedType);

  // If there are no field names to use, return no selection set.
  if (
    !Array.isArray(fieldNames) ||
    fieldNames.length === 0 ||
    !('getFields' in namedType)
  ) {
    return;
  }

  // Build a selection set of each field, calling buildSelectionSet recursively.
  return {
    kind: Kind.SELECTION_SET,
    selections: fieldNames.map(fieldName => {
      const fieldDef = namedType.getFields()[fieldName];
      const fieldType = fieldDef ? fieldDef.type : null;
      return {
        kind: Kind.FIELD,
        name: {
          kind: Kind.NAME,
          value: fieldName,
        },
        selectionSet: buildSelectionSet(fieldType, getDefaultFieldNames),
      };
    }),
  };
}

// Given an initial string, and a list of "insertion" { index, string } objects,
// return a new string with these insertions applied.
function withInsertions(initial, insertions) {
  if (insertions.length === 0) {
    return initial;
  }
  let edited = '';
  let prevIndex = 0;
  for (const { index, string } of insertions) {
    edited += initial.slice(prevIndex, index) + string;
    prevIndex = index;
  }
  edited += initial.slice(prevIndex);
  return edited;
}

// Given a string and an index, look backwards to find the string of whitespace
// following the next previous line break.
function getIndentation(str, index) {
  let indentStart = index;
  let indentEnd = index;
  while (indentStart) {
    const c = str.charCodeAt(indentStart - 1);
    // line break
    if (c === 10 || c === 13 || c === 0x2028 || c === 0x2029) {
      break;
    }
    indentStart--;
    // not white space
    if (c !== 9 && c !== 11 && c !== 12 && c !== 32 && c !== 160) {
      indentEnd = indentStart;
    }
  }
  return str.slice(indentStart, indentEnd);
}
