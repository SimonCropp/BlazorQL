/* esm.sh - graphql@17.0.2/utilities/getIntrospectionQuery */
function d(s){let e={descriptions:!0,specifiedByUrl:!1,directiveIsRepeatable:!1,schemaDescription:!1,inputValueDeprecation:!1,experimentalDirectiveDeprecation:!1,oneOf:!1,typeDepth:9,...s},n=e.descriptions?"description":"",c=e.specifiedByUrl?"specifiedByURL":"",o=e.directiveIsRepeatable?"isRepeatable":"",u=e.schemaDescription?n:"";function i(t){return e.inputValueDeprecation?t:""}function p(t){return e.experimentalDirectiveDeprecation?t:""}let l=e.oneOf?"isOneOf":"";function r(t,a){if(t<=0)return"";if(t>100)throw new Error("Please set typeDepth to a reasonable value between 0 and 100; the default is 9.");return`
${a}ofType {
${a}  name
${a}  kind${r(t-1,a+"  ")}
${a}}`}return`
    query IntrospectionQuery {
      __schema {
        ${u}
        queryType { name kind }
        mutationType { name kind }
        subscriptionType { name kind }
        types {
          ...FullType
        }
        directives${p("(includeDeprecated: true)")} {
          name
          ${n}
          ${o}
          ${p("isDeprecated")}
          ${p("deprecationReason")}
          locations
          args${i("(includeDeprecated: true)")} {
            ...InputValue
          }
        }
      }
    }

    fragment FullType on __Type {
      kind
      name
      ${n}
      ${c}
      ${l}
      fields(includeDeprecated: true) {
        name
        ${n}
        args${i("(includeDeprecated: true)")} {
          ...InputValue
        }
        type {
          ...TypeRef
        }
        isDeprecated
        deprecationReason
      }
      inputFields${i("(includeDeprecated: true)")} {
        ...InputValue
      }
      interfaces {
        ...TypeRef
      }
      enumValues(includeDeprecated: true) {
        name
        ${n}
        isDeprecated
        deprecationReason
      }
      possibleTypes {
        ...TypeRef
      }
    }

    fragment InputValue on __InputValue {
      name
      ${n}
      type { ...TypeRef }
      defaultValue
      ${i("isDeprecated")}
      ${i("deprecationReason")}
    }

    fragment TypeRef on __Type {
      kind
      name${r(e.typeDepth,"      ")}
    }
  `}export{d as getIntrospectionQuery};
//# sourceMappingURL=getIntrospectionQuery.mjs.map