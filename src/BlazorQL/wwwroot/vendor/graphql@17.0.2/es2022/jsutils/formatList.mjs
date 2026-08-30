/* esm.sh - graphql@17.0.2/jsutils/formatList */
import{invariant as e}from"./invariant.mjs";function c(r){return n("or",r)}function f(r){return n("and",r)}function n(r,t){switch(t.length===0&&e(!1),t.length){case 1:return t[0];case 2:return t[0]+" "+r+" "+t[1]}let a=t.slice(0,-1),o=t.at(-1);return a.join(", ")+", "+r+" "+o}export{f as andList,c as orList};
//# sourceMappingURL=formatList.mjs.map