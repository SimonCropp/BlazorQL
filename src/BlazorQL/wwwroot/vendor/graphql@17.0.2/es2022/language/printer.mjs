/* esm.sh - graphql@17.0.2/language/printer */
import{printBlockString as f}from"./blockString.mjs";import{printString as s}from"./printString.mjs";import{visit as D}from"./visitor.mjs";function g(e){return D(e,d)}var p=80,d={Name:{leave:e=>e.value},Variable:{leave:e=>"$"+e.name},Document:{leave:e=>i(e.definitions,`

`)},OperationDefinition:{leave(e){let n=c(e.variableDefinitions)?a(`(
`,i(e.variableDefinitions,`
`),`
)`):a("(",i(e.variableDefinitions,", "),")"),t=a("",e.description,`
`)+i([e.operation,i([e.name,n]),i(e.directives," ")]," ");return(t==="query"?"":t+" ")+e.selectionSet}},VariableDefinition:{leave:({variable:e,type:n,defaultValue:t,directives:l,description:r})=>a("",r,`
`)+e+": "+n+a(" = ",t)+a(" ",i(l," "))},SelectionSet:{leave:({selections:e})=>o(e)},Field:{leave({alias:e,name:n,arguments:t,directives:l,selectionSet:r}){let u=i([a("",e,": "),n],"");return i([m(u,t),a(" ",i(l," ")),a(" ",r)])}},Argument:{leave:({name:e,value:n})=>e+": "+n},FragmentArgument:{leave:({name:e,value:n})=>e+": "+n},FragmentSpread:{leave:({name:e,arguments:n,directives:t})=>{let l="..."+e;return m(l,n)+a(" ",i(t," "))}},InlineFragment:{leave:({typeCondition:e,directives:n,selectionSet:t})=>i(["...",a("on ",e),i(n," "),t]," ")},FragmentDefinition:{leave:({name:e,typeCondition:n,variableDefinitions:t,directives:l,selectionSet:r,description:u})=>a("",u,`
`)+`fragment ${e}${a("(",i(t,", "),")")} on ${n} ${a("",i(l," ")," ")}`+r},IntValue:{leave:({value:e})=>e},FloatValue:{leave:({value:e})=>e},StringValue:{leave:({value:e,block:n})=>n===!0?f(e):s(e)},BooleanValue:{leave:({value:e})=>e?"true":"false"},NullValue:{leave:()=>"null"},EnumValue:{leave:({value:e})=>e},ListValue:{leave:({values:e})=>{let n="["+i(e,", ")+"]";return n.length>p?`[
`+v(i(e,`
`))+`
]`:n}},ObjectValue:{leave:({fields:e})=>{let n="{ "+i(e,", ")+" }";return n.length>p?o(e):n}},ObjectField:{leave:({name:e,value:n})=>e+": "+n},Directive:{leave:({name:e,arguments:n})=>"@"+e+a("(",i(n,", "),")")},NamedType:{leave:({name:e})=>e},ListType:{leave:({type:e})=>"["+e+"]"},NonNullType:{leave:({type:e})=>e+"!"},SchemaDefinition:{leave:({description:e,directives:n,operationTypes:t})=>a("",e,`
`)+i(["schema",i(n," "),o(t)]," ")},OperationTypeDefinition:{leave:({operation:e,type:n})=>e+": "+n},ScalarTypeDefinition:{leave:({description:e,name:n,directives:t})=>a("",e,`
`)+i(["scalar",n,i(t," ")]," ")},ObjectTypeDefinition:{leave:({description:e,name:n,interfaces:t,directives:l,fields:r})=>a("",e,`
`)+i(["type",n,a("implements ",i(t," & ")),i(l," "),o(r)]," ")},FieldDefinition:{leave:({description:e,name:n,arguments:t,type:l,directives:r})=>a("",e,`
`)+n+(c(t)?a(`(
`,v(i(t,`
`)),`
)`):a("(",i(t,", "),")"))+": "+l+a(" ",i(r," "))},InputValueDefinition:{leave:({description:e,name:n,type:t,defaultValue:l,directives:r})=>a("",e,`
`)+i([n+": "+t,a("= ",l),i(r," ")]," ")},InterfaceTypeDefinition:{leave:({description:e,name:n,interfaces:t,directives:l,fields:r})=>a("",e,`
`)+i(["interface",n,a("implements ",i(t," & ")),i(l," "),o(r)]," ")},UnionTypeDefinition:{leave:({description:e,name:n,directives:t,types:l})=>a("",e,`
`)+i(["union",n,i(t," "),a("= ",i(l," | "))]," ")},EnumTypeDefinition:{leave:({description:e,name:n,directives:t,values:l})=>a("",e,`
`)+i(["enum",n,i(t," "),o(l)]," ")},EnumValueDefinition:{leave:({description:e,name:n,directives:t})=>a("",e,`
`)+i([n,i(t," ")]," ")},InputObjectTypeDefinition:{leave:({description:e,name:n,directives:t,fields:l})=>a("",e,`
`)+i(["input",n,i(t," "),o(l)]," ")},DirectiveDefinition:{leave:({description:e,name:n,arguments:t,directives:l,repeatable:r,locations:u})=>a("",e,`
`)+"directive @"+n+(c(t)?a(`(
`,v(i(t,`
`)),`
)`):a("(",i(t,", "),")"))+a(" ",i(l," "))+(r?" repeatable":"")+" on "+i(u," | ")},SchemaExtension:{leave:({directives:e,operationTypes:n})=>i(["extend schema",i(e," "),o(n)]," ")},ScalarTypeExtension:{leave:({name:e,directives:n})=>i(["extend scalar",e,i(n," ")]," ")},ObjectTypeExtension:{leave:({name:e,interfaces:n,directives:t,fields:l})=>i(["extend type",e,a("implements ",i(n," & ")),i(t," "),o(l)]," ")},InterfaceTypeExtension:{leave:({name:e,interfaces:n,directives:t,fields:l})=>i(["extend interface",e,a("implements ",i(n," & ")),i(t," "),o(l)]," ")},UnionTypeExtension:{leave:({name:e,directives:n,types:t})=>i(["extend union",e,i(n," "),a("= ",i(t," | "))]," ")},EnumTypeExtension:{leave:({name:e,directives:n,values:t})=>i(["extend enum",e,i(n," "),o(t)]," ")},InputObjectTypeExtension:{leave:({name:e,directives:n,fields:t})=>i(["extend input",e,i(n," "),o(t)]," ")},DirectiveExtension:{leave:({name:e,directives:n})=>i(["extend directive @"+e,i(n," ")]," ")},TypeCoordinate:{leave:({name:e})=>e},MemberCoordinate:{leave:({name:e,memberName:n})=>i([e,a(".",n)])},ArgumentCoordinate:{leave:({name:e,fieldName:n,argumentName:t})=>i([e,a(".",n),a("(",t,":)")])},DirectiveCoordinate:{leave:({name:e})=>i(["@",e])},DirectiveArgumentCoordinate:{leave:({name:e,argumentName:n})=>i(["@",e,a("(",n,":)")])}};function i(e,n=""){return e?.filter(t=>t!==void 0&&t!=="").join(n)??""}function o(e){return a(`{
`,v(i(e,`
`)),`
}`)}function a(e,n,t=""){return n!=null&&n!==""?e+n+t:""}function v(e){return a("  ",e.replaceAll(`
`,`
  `))}function c(e){return e?.some(n=>n.includes(`
`))??!1}function m(e,n){let t=e+a("(",i(n,", "),")");return t.length>p&&(t=e+a(`(
`,v(i(n,`
`)),`
)`)),t}export{g as print};
//# sourceMappingURL=printer.mjs.map