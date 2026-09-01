using System.Text.Json.Serialization;

/// <summary>
/// Source-generated metadata for everything the IDE puts on, or takes off, the wire.
/// </summary>
/// <remarks>
/// Call sites use the generated <c>JsonTypeInfo</c> overloads rather than passing a
/// <see cref="JsonSerializerOptions"/>, which makes a type that was never registered here a compile
/// error instead of a trimming failure discovered in a browser. It matters most for
/// <see cref="IntrospectionSchema"/>: reflection-based deserialization of a trimmed record graph
/// does not throw, it quietly yields an empty schema, and the IDE then looks like it simply has
/// nothing to say about the endpoint.
/// <para>
/// The options reproduce <see cref="JsonSerializerDefaults.Web"/> plus the null handling the
/// fetchers were already using.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(IntrospectionSchema))]
[JsonSerializable(typeof(GraphQLRequest))]
[JsonSerializable(typeof(InitFrame))]
[JsonSerializable(typeof(SubscribeFrame))]
[JsonSerializable(typeof(ErrorDocument))]
[JsonSerializable(typeof(Shortcut[]))]
[JsonSerializable(typeof(SharedQuery))]
partial class WebJson :
    JsonSerializerContext;
