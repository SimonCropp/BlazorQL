/* esm.sh - graphql@17.0.2/language/location */
import{invariant as o}from"../jsutils/invariant.mjs";var a=/\r\n|[\n\r]/g;function f(i,e){let t=0,r=1;for(let n of i.body.matchAll(a)){if(typeof n.index!="number"&&o(!1),n.index>=e)break;t=n.index+n[0].length,r+=1}return{line:r,column:e+1-t}}export{f as getLocation};
//# sourceMappingURL=location.mjs.map