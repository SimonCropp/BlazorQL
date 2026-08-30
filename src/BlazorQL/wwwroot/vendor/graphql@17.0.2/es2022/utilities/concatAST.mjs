/* esm.sh - graphql@17.0.2/utilities/concatAST */
import{Kind as t}from"../language/kinds.mjs";function f(o){let n=[];for(let i of o)n.push(...i.definitions);return{kind:t.DOCUMENT,definitions:n}}export{f as concatAST};
//# sourceMappingURL=concatAST.mjs.map