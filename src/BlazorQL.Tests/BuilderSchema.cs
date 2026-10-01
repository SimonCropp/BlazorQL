/// <summary>
/// The schema the query builder is tested over, written for it: required arguments, a connection, a
/// union and an interface, and input objects with required members. Introspected through
/// GraphQL.NET with the IDE's own query, as <see cref="ValidatorParityTests"/> does, so the builder
/// sees exactly the shape a running server would hand it.
/// </summary>
public static class BuilderSchema
{
    const string sdl =
        """
        type Query {
          user(id: ID!): User
          users(first: Int, filter: UserFilter, role: Role, ids: [ID!], roles: [Role!]): [User!]!
          search(term: String!): [SearchResult!]!
          node(id: ID!): Node
          viewer: User
          version: String
          legacy: String @deprecated(reason: "Use version.")
        }

        type Mutation {
          rename(id: ID!, name: String!): User
        }

        interface Node {
          id: ID!
        }

        type User implements Node {
          id: ID!
          name: String
          friends(first: Int): UserConnection
          role: Role
          score(scale: Float): Float
        }

        type Post implements Node {
          id: ID!
          title: String
        }

        type UserConnection {
          edges: [UserEdge]
          pageInfo: PageInfo
        }

        type UserEdge {
          node: User
          cursor: String
        }

        type PageInfo {
          hasNextPage: Boolean
        }

        union SearchResult = User | Post

        enum Role {
          ADMIN
          EDITOR
        }

        input UserFilter {
          name: String
          role: Role
          age: AgeRange
          required: String!
        }

        input AgeRange {
          min: Int!
          max: Int
        }
        """;

    // Introspected once, and parsed into an index of its own for each test that asks. SchemaIndex
    // builds its member tables lazily into plain dictionaries — right for the browser's one thread,
    // and a race between tests run in parallel over one shared instance.
    static readonly string introspection = Introspect(
        Schema.For(
            sdl,
            _ =>
            {
                // Introspection never resolves an abstract type, but the schema refuses to build
                // without a way to.
                _.Types.For("Node").ResolveType = _ => null!;
                _.Types.For("SearchResult").ResolveType = _ => null!;
            }));

    public static SchemaIndex Create()
    {
        using var document = JsonDocument.Parse(introspection);
        return SchemaIndex.Parse(document.RootElement.GetProperty("data"))!;
    }

    static string Introspect(ISchema source)
    {
        var result = new DocumentExecuter()
            .ExecuteAsync(new()
            {
                Schema = source,
                Query = BlazorQLIde.IntrospectionQuery(draftAdditions: false)
            })
            .GetAwaiter()
            .GetResult();

        return new GraphQLSerializer().Serialize(result);
    }
}
