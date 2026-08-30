/**
 * Ported from GraphiQL's @graphiql/toolkit merge-ast.ts (type annotations stripped).
 * Copyright (c) GraphQL Contributors. MIT licensed.
 * https://github.com/graphql/graphiql/blob/main/packages/graphiql-toolkit/src/graphql-helpers/merge-ast.ts
 */
// Like the vendored test schema, this takes the graphql module by injection: graphql-js relies on
// instanceof across its types, so the instance used here must be the page bundle's own.
export function createMergeAst({
  TypeInfo,
  getNamedType,
  visit,
  visitWithTypeInfo,
  Kind,
}) {

function uniqueBy(array, iteratee) {
  const FilteredMap = new Map();
  const result = [];
  for (const item of array) {
    if (item.kind === 'Field') {
      const uniqueValue = iteratee(item);
      const existing = FilteredMap.get(uniqueValue);
      if (item.directives?.length) {
        // Cannot inline fields with directives (yet)
        const itemClone = { ...item };
        result.push(itemClone);
      } else if (existing?.selectionSet && item.selectionSet) {
        // Merge the selection sets
        existing.selectionSet.selections = [
          ...existing.selectionSet.selections,
          ...item.selectionSet.selections,
        ];
      } else if (!existing) {
        const itemClone = { ...item };
        FilteredMap.set(uniqueValue, itemClone);
        result.push(itemClone);
      }
    } else {
      result.push(item);
    }
  }
  return result;
}

function inlineRelevantFragmentSpreads(
  fragmentDefinitions,
  selections,
  selectionSetType,
) {
  const selectionSetTypeName = selectionSetType
    ? getNamedType(selectionSetType).name
    : null;
  const outputSelections = [];
  const seenSpreads = [];
  for (let selection of selections) {
    if (selection.kind === 'FragmentSpread') {
      const fragmentName = selection.name.value;
      if (!selection.directives || selection.directives.length === 0) {
        if (seenSpreads.includes(fragmentName)) {
          /* It's a duplicate - skip it! */
          continue;
        } else {
          seenSpreads.push(fragmentName);
        }
      }
      const fragmentDefinition = fragmentDefinitions[selection.name.value];
      if (fragmentDefinition) {
        const { typeCondition, directives, selectionSet } = fragmentDefinition;
        selection = {
          kind: Kind.INLINE_FRAGMENT,
          typeCondition,
          directives,
          selectionSet,
        };
      }
    }
    if (
      selection.kind === Kind.INLINE_FRAGMENT &&
      // Cannot inline if there are directives
      (!selection.directives || selection.directives?.length === 0)
    ) {
      const fragmentTypeName = selection.typeCondition
        ? selection.typeCondition.name.value
        : null;
      if (!fragmentTypeName || fragmentTypeName === selectionSetTypeName) {
        outputSelections.push(
          ...inlineRelevantFragmentSpreads(
            fragmentDefinitions,
            selection.selectionSet.selections,
            selectionSetType,
          ),
        );
        continue;
      }
    }
    outputSelections.push(selection);
  }
  return outputSelections;
}

/**
 * Given a document AST, inline all named fragment definitions.
 */
function mergeAst(documentAST, schema) {
  // If we're given the schema, we can simplify even further by resolving object
  // types vs unions/interfaces
  const typeInfo = schema ? new TypeInfo(schema) : null;

  const fragmentDefinitions = Object.create(null);

  for (const definition of documentAST.definitions) {
    if (definition.kind === Kind.FRAGMENT_DEFINITION) {
      fragmentDefinitions[definition.name.value] = definition;
    }
  }

  const flattenVisitors = {
    SelectionSet(node) {
      const selectionSetType = typeInfo ? typeInfo.getParentType() : null;
      let { selections } = node;

      selections = inlineRelevantFragmentSpreads(
        fragmentDefinitions,
        selections,
        selectionSetType,
      );

      return {
        ...node,
        selections,
      };
    },
    FragmentDefinition() {
      return null;
    },
  };

  const flattenedAST = visit(
    documentAST,
    typeInfo ? visitWithTypeInfo(typeInfo, flattenVisitors) : flattenVisitors,
  );

  const deduplicateVisitors = {
    SelectionSet(node) {
      let { selections } = node;

      selections = uniqueBy(selections, selection =>
        selection.alias ? selection.alias.value : selection.name.value,
      );

      return {
        ...node,
        selections,
      };
    },
    FragmentDefinition() {
      return null;
    },
  };

  return visit(flattenedAST, deduplicateVisitors);
}

return mergeAst;
}
