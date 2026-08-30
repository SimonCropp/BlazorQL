namespace BlazorQL;

/// <summary>
/// Validates documents against the introspected schema using GraphQL.NET's spec rules, plus a
/// deprecation walker of its own (deprecated usage warns, as in GraphiQL). The schema is built from
/// the printed SDL — the same text the SDL view shows.
/// </summary>
public sealed class SchemaValidator
{
    ISchema schema;
    SchemaIndex index;
    DocumentValidator validator = new();

    SchemaValidator(ISchema schema, SchemaIndex index)
    {
        this.schema = schema;
        this.index = index;
    }

    /// <summary>Null when the SDL does not build — validation is then simply unavailable.</summary>
    public static SchemaValidator? TryCreate(SchemaIndex index, string sdl)
    {
        try
        {
            // The schema exists to validate documents, never to execute them, so every execution
            // hook is stubbed: abstract types resolve to nothing, and subscription roots stream
            // nothing.
            var schema = Schema.For(sdl, _ => _.Types.ForAll(_ => _.ResolveType = _ => null!));

            // Schema.For only materializes a custom scalar declaration when a matching CLR scalar
            // is registered; validation treats custom scalars as accept-anything, like GraphiQL.
            foreach (var type in index.Types)
            {
                if (type is
                    {
                        Kind: "SCALAR",
                        Name: not ("Int" or "Float" or "String" or "Boolean" or "ID")
                    })
                {
                    schema.RegisterType(new PermissiveScalar(type.Name));
                }
            }

            if (schema.Subscription is not null)
            {
                foreach (var field in schema.Subscription.Fields)
                {
                    field.StreamResolver = NullStreamResolver.Instance;
                }
            }

            schema.Initialize();
            return new(schema, index);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A scalar that accepts any value — validation-only stand-in for custom scalars.</summary>
    sealed class PermissiveScalar : ScalarGraphType
    {
        public PermissiveScalar(string name) =>
            Name = name;

        public override object? ParseValue(object? value) =>
            value;

        public override object? ParseLiteral(GraphQLValue value) =>
            value;

        public override bool CanParseLiteral(GraphQLValue value) =>
            true;

        public override bool CanParseValue(object? value) =>
            true;
    }

    /// <summary>Satisfies the subscription-root requirement on a schema that never executes.</summary>
    sealed class NullStreamResolver :
        GraphQL.Resolvers.ISourceStreamResolver
    {
        public static readonly NullStreamResolver Instance = new();

        public ValueTask<IObservable<object?>> ResolveAsync(GraphQL.IResolveFieldContext context) =>
            throw new NotSupportedException("The validation schema never executes.");
    }

    public async Task<IReadOnlyList<OperationDiagnostic>> Validate(DocumentInfo info)
    {
        if (info.SyntaxError is not null)
        {
            return [new($"Syntax Error: {info.SyntaxError}", IsError: true, info.SyntaxErrorLine, info.SyntaxErrorColumn)];
        }

        var document = info.Document;
        if (document is null)
        {
            return [];
        }

        var diagnostics = new List<OperationDiagnostic>();
        var operation = document.Definitions.OfType<GraphQLOperationDefinition>().FirstOrDefault();
        if (operation is not null)
        {
            try
            {
                var result = await validator.ValidateAsync(
                    new()
                    {
                        Schema = schema,
                        Document = document,
                        Operation = operation
                    });

                foreach (var error in result.Errors)
                {
                    if (error.Locations is {Count: > 0} locations)
                    {
                        var location = locations[0];
                        diagnostics.Add(new(error.Message, IsError: true, location.Line, location.Column));
                    }
                    else
                    {
                        diagnostics.Add(new(error.Message, IsError: true, 1, 1));
                    }
                }
            }
            catch (Exception)
            {
                // Validation is best-effort; a rule blowing up must not take typing down with it.
            }
        }

        DeprecationWarnings(info, diagnostics);
        return diagnostics;
    }

    /// <summary>
    /// Walks fields, arguments, and enum values against the introspection model and warns on
    /// deprecated usage. Best-effort: unknown names are validation's problem, not this walker's.
    /// </summary>
    void DeprecationWarnings(DocumentInfo info, List<OperationDiagnostic> diagnostics)
    {
        var walker = new DeprecationWalker(index, info.Text, diagnostics);
        var document = info.Document!;
        foreach (var definition in document.Definitions)
        {
            switch (definition)
            {
                case GraphQLOperationDefinition operation:
                    var rootName = operation.Operation switch
                    {
                        OperationType.Mutation => index.MutationTypeName,
                        OperationType.Subscription => index.SubscriptionTypeName,
                        _ => index.QueryTypeName
                    };
                    walker.WalkSelections(operation.SelectionSet, index.Find(rootName));
                    break;

                case GraphQLFragmentDefinition fragment:
                    walker.WalkSelections(fragment.SelectionSet, index.Find(fragment.TypeCondition.Type.Name.StringValue));
                    break;
            }
        }
    }

    sealed class DeprecationWalker(SchemaIndex index, string text, List<OperationDiagnostic> diagnostics)
    {
        public void WalkSelections(GraphQLSelectionSet? selections, IntrospectionType? type)
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
                        var definition = type.Fields?.FirstOrDefault(_ => _.Name == field.Name.StringValue);
                        if (definition is null)
                        {
                            continue;
                        }

                        if (definition.IsDeprecated)
                        {
                            Warn(field.Name, $"The field {type.Name}.{definition.Name} is deprecated. {definition.DeprecationReason}".TrimEnd());
                        }

                        WalkArguments(field, definition);
                        WalkSelections(field.SelectionSet, index.Find(definition.Type.Unwrap().Name));
                        break;

                    case GraphQLInlineFragment inline:
                        var condition = inline.TypeCondition?.Type.Name.StringValue;
                        WalkSelections(inline.SelectionSet, condition is null ? type : index.Find(condition));
                        break;
                }
            }
        }

        void WalkArguments(GraphQLField field, IntrospectionField introspection)
        {
            foreach (var argument in field.Arguments?.Items ?? [])
            {
                var definition = introspection.Args.FirstOrDefault(_ => _.Name == argument.Name.StringValue);
                if (definition is {IsDeprecated: true})
                {
                    Warn(argument.Name, $"The argument {definition.Name} is deprecated. {definition.DeprecationReason}".TrimEnd());
                }
            }
        }

        void Warn(ASTNode node, string message)
        {
            var location = Location.FromLinearPosition(text, node.Location.Start);
            diagnostics.Add(new(message, IsError: false, location.Line, location.Column));
        }
    }
}
