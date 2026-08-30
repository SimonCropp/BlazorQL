/* esm.sh - graphql@17.0.2/utilities/getOperationAST */
import{Kind as r}from"../language/kinds.mjs";function o(t,e){let i=null;for(let n of t.definitions)if(n.kind===r.OPERATION_DEFINITION){if(e==null){if(i)return null;i=n}else if(n.name?.value===e)return n}return i}export{o as getOperationAST};
//# sourceMappingURL=getOperationAST.mjs.map