/* esm.sh - graphql@17.0.2/execution/collectIteratorPromises */
import{isPromiseLike as o}from"../jsutils/isPromise.mjs";function n(r){let e=[];try{for(;;){let t=r.next();if(t.done)return e;o(t.value)&&e.push(t.value)}}catch{return e}}export{n as collectIteratorPromises};
//# sourceMappingURL=collectIteratorPromises.mjs.map