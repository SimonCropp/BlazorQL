/* esm.sh - graphql@17.0.2/jsutils/toError */
import{inspect as o}from"./inspect.mjs";function s(r){return r instanceof Error?r:new e(r)}var e=class extends Error{constructor(t){super("Unexpected error value: "+o(t)),this.name="NonErrorThrown",this.thrownValue=t}};export{s as toError};
//# sourceMappingURL=toError.mjs.map