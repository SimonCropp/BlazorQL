/* esm.sh - graphql@17.0.2/jsutils/instanceOf */
import{inspect as i}from"./inspect.mjs";function a(e,n,t){if(e?.__kind===n)return!0;if(typeof e=="object"&&e!==null){let o=t.prototype[Symbol.toStringTag],r=Symbol.toStringTag in e?e[Symbol.toStringTag]:e.constructor?.name;if(o===r){let s=i(e);throw new Error(`Cannot use ${o} "${s}" from another module or realm.

Ensure that there is only one instance of "graphql" in the node_modules
directory. If different versions of "graphql" are the dependencies of other
relied on modules, use "resolutions" to ensure only one version is installed.

https://yarnpkg.com/en/docs/selective-version-resolutions

Duplicate "graphql" modules cannot be used at the same time since different
versions may have different capabilities and behavior. The data from one
version used in the function from another could produce confusing and
spurious results.`)}}return!1}function c(e,n){return e?.__kind===n}var f=c;function l(){f=a}export{l as enableDevInstanceOf,f as instanceOf};
//# sourceMappingURL=instanceOf.mjs.map