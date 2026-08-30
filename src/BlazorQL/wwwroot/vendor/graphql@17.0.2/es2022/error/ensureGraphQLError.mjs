/* esm.sh - graphql@17.0.2/error/ensureGraphQLError */
import{toError as e}from"../jsutils/toError.mjs";import{GraphQLError as n}from"./GraphQLError.mjs";function f(r){if(r instanceof n)return r;let o=e(r);return new n(o.message,{originalError:o})}export{f as ensureGraphQLError};
//# sourceMappingURL=ensureGraphQLError.mjs.map