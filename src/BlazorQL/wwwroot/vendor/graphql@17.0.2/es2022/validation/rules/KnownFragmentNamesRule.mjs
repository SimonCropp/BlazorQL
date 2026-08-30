/* esm.sh - graphql@17.0.2/validation/rules/KnownFragmentNamesRule */
import{GraphQLError as a}from"../../error/GraphQLError.mjs";function o(r){return{FragmentSpread(n){let e=n.name.value;r.getFragment(e)||r.reportError(new a(`Unknown fragment "${e}".`,{nodes:n.name}))}}}export{o as KnownFragmentNamesRule};
//# sourceMappingURL=KnownFragmentNamesRule.mjs.map