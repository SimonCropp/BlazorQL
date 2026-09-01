/// <summary>
/// The <c>connection_init</c> frame. The request headers travel in its payload, where the
/// graphql-transport-ws protocol puts them.
/// </summary>
sealed record InitFrame(string Type, IReadOnlyDictionary<string, string> Payload);

/// <summary>
/// The <c>subscribe</c> frame. Its payload is the request itself, which is already the shape the
/// protocol asks for.
/// </summary>
sealed record SubscribeFrame(string Id, string Type, GraphQLRequest Payload);
