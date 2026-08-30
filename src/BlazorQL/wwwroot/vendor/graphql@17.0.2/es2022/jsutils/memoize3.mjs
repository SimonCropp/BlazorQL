/* esm.sh - graphql@17.0.2/jsutils/memoize3 */
function o(d){let i;return function(f,c,u){i??=new WeakMap;let e=i.get(f);e===void 0&&(e=new WeakMap,i.set(f,e));let n=e.get(c);n===void 0&&(n=new WeakMap,e.set(c,n));let t=n.get(u);return t===void 0&&(t=d(f,c,u),n.set(u,t)),t}}export{o as memoize3};
//# sourceMappingURL=memoize3.mjs.map