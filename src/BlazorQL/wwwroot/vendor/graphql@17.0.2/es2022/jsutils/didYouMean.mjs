/* esm.sh - graphql@17.0.2/jsutils/didYouMean */
import{orList as a}from"./formatList.mjs";var g=5;function m(e,n){let[s,t]=n?[e,n]:[void 0,e];if(t.length===0)return"";let i=" Did you mean ";s!=null&&(i+=s+" ");let o=a(t.slice(0,g).map(u=>`"${u}"`));return i+o+"?"}export{m as didYouMean};
//# sourceMappingURL=didYouMean.mjs.map