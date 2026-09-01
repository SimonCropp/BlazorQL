namespace BlazorQL;

/// <summary>
/// Validates documents against the introspected schema, and warns on deprecated usage the way
/// GraphiQL does. The rules are the subset of the spec an editor can act on: everything that marks a
/// span of text the author can see and fix.
/// </summary>
/// <remarks>
/// Implemented over GraphQLParser and <see cref="SchemaIndex"/> rather than a schema object model.
/// Building one meant carrying GraphQL.NET, which cost roughly 2 MB once its XML-documentation and
/// expression dependencies are counted, and which did not survive trimming: in a published Blazor
/// WebAssembly build its validator threw NullReferenceException for any operation declaring
/// variables, so validation silently produced nothing for the shape an IDE session uses most.
/// <para>
/// Messages follow graphql-js wording, because that is what GraphiQL shows and what the parity goal
/// measures against.
/// </para>
/// </remarks>
public sealed class SchemaValidator(SchemaIndex index)
{
    /// <summary>Diagnostics for one document. Errors mark mistakes, warnings mark deprecated usage.</summary>
    public IReadOnlyList<OperationDiagnostic> Validate(DocumentInfo info)
    {
        if (info.SyntaxError is not null)
        {
            return [new($"Syntax Error: {info.SyntaxError}", IsError: true, info.SyntaxErrorLine, info.SyntaxErrorColumn)];
        }

        if (info.Document is null)
        {
            return [];
        }

        var diagnostics = new List<OperationDiagnostic>();
        new Walker(index, info.Text, diagnostics).WalkDocument(info.Document);
        return diagnostics;
    }

    /// <summary>
    /// One pass over the document. Fragments are collected up front, because a spread can precede the
    /// definition it names, and because "never used" can only be answered once everything is in.
    /// </summary>
    sealed class Walker(SchemaIndex index, string text, List<OperationDiagnostic> diagnostics)
    {
        readonly Dictionary<string, GraphQLFragmentDefinition> fragments = new(StringComparer.Ordinal);
        readonly HashSet<string> spreadFragments = new(StringComparer.Ordinal);
        readonly HashSet<string> declaredVariables = new(StringComparer.Ordinal);
        readonly HashSet<string> usedVariables = new(StringComparer.Ordinal);
        readonly Dictionary<string, GraphQLType> variableTypes = new(StringComparer.Ordinal);

        public void WalkDocument(GraphQLDocument document)
        {
            foreach (var fragment in document.Definitions.OfType<GraphQLFragmentDefinition>())
            {
                fragments.TryAdd(fragment.FragmentName.Name.StringValue, fragment);
            }

            var operations = document.Definitions.OfType<GraphQLOperationDefinition>().ToList();
            CheckOperationNames(operations);

            foreach (var operation in operations)
            {
                WalkOperation(operation);
            }

            foreach (var fragment in fragments.Values)
            {
                WalkFragmentDefinition(fragment);
            }

            foreach (var fragment in fragments.Values)
            {
                var name = fragment.FragmentName.Name.StringValue;
                if (!spreadFragments.Contains(name))
                {
                    Error(fragment.FragmentName, $"Fragment \"{name}\" is never used.");
                }
            }
        }

        /// <summary>UniqueOperationNames and LoneAnonymousOperation.</summary>
        void CheckOperationNames(List<GraphQLOperationDefinition> operations)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in operations)
            {
                if (operation.Name is not {} name)
                {
                    if (operations.Count > 1)
                    {
                        Error(operation, "This anonymous operation must be the only defined operation.");
                    }

                    continue;
                }

                if (!seen.Add(name.StringValue))
                {
                    Error(name, $"There can be only one operation named \"{name.StringValue}\".");
                }
            }
        }

        void WalkOperation(GraphQLOperationDefinition operation)
        {
            declaredVariables.Clear();
            usedVariables.Clear();
            variableTypes.Clear();

            foreach (var variable in operation.Variables?.Items ?? [])
            {
                var name = variable.Variable.Name.StringValue;
                declaredVariables.Add(name);
                variableTypes[name] = variable.Type;
                CheckVariableIsInputType(variable);
            }

            var rootName = operation.Operation switch
            {
                OperationType.Mutation => index.MutationTypeName,
                OperationType.Subscription => index.SubscriptionTypeName,
                _ => index.QueryTypeName
            };

            var root = index.Find(rootName);
            if (root is null)
            {
                var what = operation.Operation.ToString().ToLowerInvariant();
                Error(operation, $"Schema is not configured for {what}s.");
                return;
            }

            CheckDirectives(operation.Directives);
            WalkSelections(operation.SelectionSet, root);

            foreach (var name in declaredVariables)
            {
                if (!usedVariables.Contains(name))
                {
                    Error(operation, $"Variable \"${name}\" is never used.");
                }
            }
        }

        void WalkFragmentDefinition(GraphQLFragmentDefinition fragment)
        {
            var conditionName = fragment.TypeCondition.Type.Name.StringValue;
            var condition = index.Find(conditionName);
            if (condition is null)
            {
                Error(fragment.TypeCondition.Type, $"Unknown type \"{conditionName}\".");
                return;
            }

            if (!IsComposite(condition))
            {
                Error(
                    fragment.TypeCondition.Type,
                    $"Fragment \"{fragment.FragmentName.Name.StringValue}\" cannot condition on non composite type \"{conditionName}\".");
                return;
            }

            CheckDirectives(fragment.Directives);
            WalkSelections(fragment.SelectionSet, condition);
        }

        void WalkSelections(GraphQLSelectionSet? selections, IntrospectionType? type)
        {
            if (selections is null ||
                type is null)
            {
                return;
            }

            foreach (var selection in selections.Selections)
            {
                switch (selection)
                {
                    case GraphQLField field:
                        WalkField(field, type);
                        break;

                    case GraphQLFragmentSpread spread:
                        WalkSpread(spread);
                        break;

                    case GraphQLInlineFragment inline:
                        WalkInlineFragment(inline, type);
                        break;
                }
            }
        }

        void WalkField(GraphQLField field, IntrospectionType parent)
        {
            CheckDirectives(field.Directives);

            var name = field.Name.StringValue;

            // Introspection does not report the meta-fields as ordinary fields: __typename is
            // available on every composite type, and the two schema-introspection fields on the
            // query root only.
            if (name == "__typename")
            {
                CheckNoSubSelection(field, "String");
                return;
            }

            if (parent.Name == index.QueryTypeName &&
                name is "__schema" or "__type")
            {
                return;
            }

            var definition = parent.Fields?.FirstOrDefault(_ => _.Name == name);
            if (definition is null)
            {
                // A union carries no fields of its own, so the author has to narrow first. Saying so
                // is more use than "cannot query field".
                var hint = parent.Kind == "UNION"
                    ? " Did you mean to use an inline fragment on one of its possible types?"
                    : "";
                Error(field.Name, $"Cannot query field \"{name}\" on type \"{parent.Name}\".{hint}");
                return;
            }

            if (definition.IsDeprecated)
            {
                Warn(
                    field.Name,
                    $"The field {parent.Name}.{definition.Name} is deprecated. {definition.DeprecationReason}".TrimEnd());
            }

            WalkArguments(field, definition.Args, $"{parent.Name}.{name}");

            var fieldType = index.Find(definition.Type.Unwrap().Name);
            if (fieldType is null)
            {
                return;
            }

            if (IsComposite(fieldType))
            {
                if (field.SelectionSet is null)
                {
                    Error(
                        field.Name,
                        $"Field \"{name}\" of type \"{definition.Type.Display()}\" must have a selection of subfields. Did you mean \"{name} {{ ... }}\"?");
                    return;
                }

                WalkSelections(field.SelectionSet, fieldType);
                return;
            }

            CheckNoSubSelection(field, definition.Type.Display());
        }

        void CheckNoSubSelection(GraphQLField field, string typeName)
        {
            if (field.SelectionSet is not null)
            {
                Error(
                    field.Name,
                    $"Field \"{field.Name.StringValue}\" must not have a selection since type \"{typeName}\" has no subfields.");
            }
        }

        /// <summary>KnownArgumentNames, ProvidedRequiredArguments, and the argument values.</summary>
        void WalkArguments(GraphQLField field, IReadOnlyList<IntrospectionInputValue> declared, string where)
        {
            foreach (var argument in field.Arguments?.Items ?? [])
            {
                var name = argument.Name.StringValue;
                var definition = declared.FirstOrDefault(_ => _.Name == name);
                if (definition is null)
                {
                    Error(argument.Name, $"Unknown argument \"{name}\" on field \"{where}\".");
                    continue;
                }

                if (definition.IsDeprecated)
                {
                    Warn(
                        argument.Name,
                        $"The argument {definition.Name} is deprecated. {definition.DeprecationReason}".TrimEnd());
                }

                CheckValue(argument.Value, definition.Type);
            }

            foreach (var definition in declared)
            {
                if (definition.Type.Kind != "NON_NULL" ||
                    definition.DefaultValue is not null)
                {
                    continue;
                }

                if (field.Arguments?.Items.Any(_ => _.Name.StringValue == definition.Name) != true)
                {
                    Error(
                        field.Name,
                        $"Field \"{where}\" argument \"{definition.Name}\" of type \"{definition.Type.Display()}\" is required, but it was not provided.");
                }
            }
        }

        void WalkSpread(GraphQLFragmentSpread spread)
        {
            var name = spread.FragmentName.Name.StringValue;
            spreadFragments.Add(name);
            CheckDirectives(spread.Directives);

            if (!fragments.ContainsKey(name))
            {
                Error(spread.FragmentName, $"Unknown fragment \"{name}\".");
            }
        }

        void WalkInlineFragment(GraphQLInlineFragment inline, IntrospectionType enclosing)
        {
            CheckDirectives(inline.Directives);

            if (inline.TypeCondition is null)
            {
                WalkSelections(inline.SelectionSet, enclosing);
                return;
            }

            var conditionName = inline.TypeCondition.Type.Name.StringValue;
            var condition = index.Find(conditionName);
            if (condition is null)
            {
                Error(inline.TypeCondition.Type, $"Unknown type \"{conditionName}\".");
                return;
            }

            if (!IsComposite(condition))
            {
                Error(
                    inline.TypeCondition.Type,
                    $"Fragment cannot condition on non composite type \"{conditionName}\".");
                return;
            }

            WalkSelections(inline.SelectionSet, condition);
        }

        /// <summary>VariablesAreInputTypes, and KnownTypeNames for the declared type.</summary>
        void CheckVariableIsInputType(GraphQLVariableDefinition variable)
        {
            if (NamedType(variable.Type) is not {} named)
            {
                return;
            }

            var typeName = named.Name.StringValue;
            var type = index.Find(typeName);
            if (type is null)
            {
                Error(named, $"Unknown type \"{typeName}\".");
                return;
            }

            if (type.Kind is not ("SCALAR" or "ENUM" or "INPUT_OBJECT"))
            {
                Error(
                    named,
                    $"Variable \"${variable.Variable.Name.StringValue}\" cannot be non-input type \"{Render(variable.Type)}\".");
            }
        }

        void CheckDirectives(GraphQLDirectives? directives)
        {
            foreach (var directive in directives?.Items ?? [])
            {
                var name = directive.Name.StringValue;
                var declared = index.Directives.FirstOrDefault(_ => _.Name == name);
                if (declared is null)
                {
                    Error(directive.Name, $"Unknown directive \"@{name}\".");
                    continue;
                }

                foreach (var argument in directive.Arguments?.Items ?? [])
                {
                    var definition = declared.Args.FirstOrDefault(_ => _.Name == argument.Name.StringValue);
                    if (definition is null)
                    {
                        Error(
                            argument.Name,
                            $"Unknown argument \"{argument.Name.StringValue}\" on directive \"@{name}\".");
                        continue;
                    }

                    CheckValue(argument.Value, definition.Type);
                }
            }
        }

        /// <summary>
        /// ValuesOfCorrectType for literals, and VariablesInAllowedPosition wherever the literal is a
        /// variable reference.
        /// </summary>
        void CheckValue(GraphQLValue value, TypeRef expected)
        {
            if (value is GraphQLVariable variable)
            {
                CheckVariableUse(variable, expected);
                return;
            }

            if (expected.Kind == "NON_NULL")
            {
                if (value is GraphQLNullValue)
                {
                    Error(value, $"Expected value of type \"{expected.Display()}\", found null.");
                    return;
                }

                CheckValue(value, expected.OfType!);
                return;
            }

            if (value is GraphQLNullValue)
            {
                return;
            }

            if (expected.Kind == "LIST")
            {
                if (value is GraphQLListValue list)
                {
                    foreach (var item in list.Values ?? [])
                    {
                        CheckValue(item, expected.OfType!);
                    }

                    return;
                }

                // A single value coerces to a one-element list, per spec.
                CheckValue(value, expected.OfType!);
                return;
            }

            if (index.Find(expected.Name) is not {} type)
            {
                return;
            }

            switch (type.Kind)
            {
                case "ENUM":
                    CheckEnum(value, type);
                    return;

                case "INPUT_OBJECT":
                    CheckInputObject(value, type);
                    return;

                case "SCALAR":
                    CheckScalar(value, type);
                    return;
            }
        }

        void CheckVariableUse(GraphQLVariable variable, TypeRef expected)
        {
            var name = variable.Name.StringValue;
            usedVariables.Add(name);
            if (!variableTypes.TryGetValue(name, out var declared))
            {
                Error(variable, $"Variable \"${name}\" is not defined.");
                return;
            }

            if (!Accepts(expected, declared))
            {
                Error(
                    variable,
                    $"Variable \"${name}\" of type \"{Render(declared)}\" used in position expecting type \"{expected.Display()}\".");
            }
        }

        void CheckEnum(GraphQLValue value, IntrospectionType type)
        {
            if (value is not GraphQLEnumValue enumValue)
            {
                Error(value, $"Enum \"{type.Name}\" cannot represent non-enum value.");
                return;
            }

            var literal = enumValue.Name.StringValue;
            var declared = type.EnumValues?.FirstOrDefault(_ => _.Name == literal);
            if (declared is null)
            {
                Error(value, $"Value \"{literal}\" does not exist in \"{type.Name}\" enum.");
                return;
            }

            if (declared.IsDeprecated)
            {
                Warn(
                    value,
                    $"The enum value {type.Name}.{literal} is deprecated. {declared.DeprecationReason}".TrimEnd());
            }
        }

        void CheckInputObject(GraphQLValue value, IntrospectionType type)
        {
            if (value is not GraphQLObjectValue objectValue)
            {
                Error(value, $"Expected value of type \"{type.Name}\", found a non-object value.");
                return;
            }

            var fields = objectValue.Fields ?? [];
            foreach (var field in fields)
            {
                var name = field.Name.StringValue;
                var definition = type.InputFields?.FirstOrDefault(_ => _.Name == name);
                if (definition is null)
                {
                    Error(field.Name, $"Field \"{name}\" is not defined by type \"{type.Name}\".");
                    continue;
                }

                CheckValue(field.Value, definition.Type);
            }

            foreach (var definition in type.InputFields ?? [])
            {
                if (definition.Type.Kind != "NON_NULL" ||
                    definition.DefaultValue is not null)
                {
                    continue;
                }

                if (fields.All(_ => _.Name.StringValue != definition.Name))
                {
                    Error(
                        value,
                        $"Field \"{type.Name}.{definition.Name}\" of required type \"{definition.Type.Display()}\" was not provided.");
                }
            }
        }

        /// <summary>
        /// Only the five built-in scalars are checked. A custom scalar's literal grammar belongs to
        /// the server, and guessing at it produces false errors — which are worse than none.
        /// </summary>
        void CheckScalar(GraphQLValue value, IntrospectionType type)
        {
            var ok = type.Name switch
            {
                "Int" => value is GraphQLIntValue,
                "Float" => value is GraphQLFloatValue or GraphQLIntValue,
                "String" => value is GraphQLStringValue,
                "Boolean" => value is GraphQLBooleanValue,
                "ID" => value is GraphQLStringValue or GraphQLIntValue,
                _ => true
            };

            if (!ok)
            {
                Error(value, $"{type.Name} cannot represent a value of this kind.");
            }
        }

        /// <summary>
        /// Whether a variable declared as <paramref name="actual"/> may be used where
        /// <paramref name="expected"/> is wanted. A non-null variable satisfies a nullable position,
        /// but not the other way round.
        /// </summary>
        static bool Accepts(TypeRef expected, GraphQLType actual)
        {
            if (actual is GraphQLNonNullType nonNullActual)
            {
                if (expected.Kind == "NON_NULL")
                {
                    return Accepts(expected.OfType!, nonNullActual.Type);
                }

                return Accepts(expected, nonNullActual.Type);
            }

            if (expected.Kind == "NON_NULL")
            {
                return false;
            }

            if (expected.Kind == "LIST")
            {
                return actual is GraphQLListType list && Accepts(expected.OfType!, list.Type);
            }

            return actual is GraphQLNamedType named &&
                   named.Name.StringValue == expected.Name;
        }

        static bool IsComposite(IntrospectionType type) =>
            type.Kind is "OBJECT" or "INTERFACE" or "UNION";

        static GraphQLNamedType? NamedType(GraphQLType type) =>
            type switch
            {
                GraphQLNamedType named => named,
                GraphQLNonNullType nonNull => NamedType(nonNull.Type),
                GraphQLListType list => NamedType(list.Type),
                _ => null
            };

        static string Render(GraphQLType type) =>
            type switch
            {
                GraphQLNonNullType nonNull => $"{Render(nonNull.Type)}!",
                GraphQLListType list => $"[{Render(list.Type)}]",
                GraphQLNamedType named => named.Name.StringValue,
                _ => ""
            };

        void Error(ASTNode node, string message) =>
            Add(node, message, isError: true);

        void Warn(ASTNode node, string message) =>
            Add(node, message, isError: false);

        void Add(ASTNode node, string message, bool isError)
        {
            var location = Location.FromLinearPosition(text, node.Location.Start);
            diagnostics.Add(new(message, isError, location.Line, location.Column));
        }
    }
}
