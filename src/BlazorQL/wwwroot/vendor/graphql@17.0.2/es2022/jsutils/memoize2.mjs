/* esm.sh - graphql@17.0.2/jsutils/memoize2 */
function d(u){let t;return function(i,f){t??=new WeakMap;let e=t.get(i);e===void 0&&(e=new WeakMap,t.set(i,e));let n=e.get(f);return n===void 0&&(n=u(i,f),e.set(f,n)),n}}export{d as memoize2};
//# sourceMappingURL=memoize2.mjs.map