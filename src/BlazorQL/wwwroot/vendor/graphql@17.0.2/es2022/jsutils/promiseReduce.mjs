/* esm.sh - graphql@17.0.2/jsutils/promiseReduce */
import{isPromise as m}from"./isPromise.mjs";function n(t,e,u){let o=u;for(let r of t)o=m(o)?o.then(i=>e(i,r)):e(o,r);return o}export{n as promiseReduce};
//# sourceMappingURL=promiseReduce.mjs.map