/* esm.sh - graphql@17.0.2/execution/hooks */
function e(n,r){try{n?.(r)}catch{}}function o(n,r,c){let t=r.asyncWorkTracker.wait();if(t===void 0){e(c,{validatedExecutionArgs:n});return}t.then(()=>{e(c,{validatedExecutionArgs:n})}).catch(()=>{})}export{o as runAsyncWorkFinishedHook};
//# sourceMappingURL=hooks.mjs.map