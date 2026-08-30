/* esm.sh - graphql@17.0.2/error/locatedError */
import{toError as e}from"../jsutils/toError.mjs";import{GraphQLError as i}from"./GraphQLError.mjs";function c(o,t,n){let r=e(o);return s(r)?r:new i(r.message,{nodes:r.nodes??t,source:r.source,positions:r.positions,path:n,originalError:r})}function s(o){return Array.isArray(o.path)}export{c as locatedError};
//# sourceMappingURL=locatedError.mjs.map