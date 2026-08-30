/* esm.sh - monaco-graphql@1.8.0/esm/initialize */
import{create as r}from"./api.mjs";import{languages as i}from"../monaco-editor.mjs";var a="graphql",e;function u(t){return e||(e=r(a,t),i.graphql={api:e},n().then(o=>o.setupMode(e))),e}function n(){return import("./graphqlMode.mjs")}export{a as LANGUAGE_ID,u as initializeMode};
//# sourceMappingURL=initialize.mjs.map