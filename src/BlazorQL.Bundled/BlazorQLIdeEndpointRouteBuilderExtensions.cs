using System.Diagnostics.CodeAnalysis;

namespace BlazorQL;

/// <summary>Mounts the BlazorQL IDE in an ASP.NET Core app.</summary>
public static class BlazorQLIdeEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Serves the IDE at <paramref name="pattern"/>. The whole WebAssembly application is embedded
    /// in this assembly, so there is nothing to deploy alongside it and no Blazor SDK involved.
    /// </summary>
    /// <example>
    /// <code>app.MapBlazorQL("/graphql-ide", _ => _.Endpoint = "/graphql");</code>
    /// </example>
    /// <returns>
    /// The mounted endpoints, so conventions apply to both of them:
    /// <c>app.MapBlazorQL().RequireAuthorization("Admin")</c>.
    /// </returns>
    public static IEndpointConventionBuilder MapBlazorQL(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern = "/graphql-ide",
        Action<BlazorQLIdeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);

        var options = new BlazorQLIdeOptions();
        configure?.Invoke(options);

        var prefix = pattern.TrimEnd('/');
        if (prefix.Length > 0 &&
            !prefix.StartsWith('/'))
        {
            prefix = '/' + prefix;
        }

        var endpoint = new IdeEndpoint(options, prefix);

        var root = endpoints.MapMethods(
            prefix.Length == 0 ? "/" : prefix,
            [HttpMethods.Get, HttpMethods.Head],
            (HttpContext context) => Root(context, endpoint, prefix));

        // A double-star catch-all, because {*path} does not round-trip slashes unescaped.
        var assets = endpoints.MapMethods(
            $"{prefix}/{{**path}}",
            [HttpMethods.Get, HttpMethods.Head],
            endpoint.Handle);

        root.WithDisplayName("BlazorQL IDE");
        assets.WithDisplayName("BlazorQL IDE assets");

        return new CompositeConventionBuilder([root, assets]);
    }

    /// <summary>
    /// The bare mount. Routing ignores a trailing slash when matching, so this endpoint answers both
    /// <c>/graphql-ide</c> and <c>/graphql-ide/</c> and has to tell them apart itself — redirecting
    /// unconditionally would send the slashed form to itself forever.
    /// </summary>
    /// <remarks>
    /// The redirect is worth keeping for the unslashed form: it puts location.pathname inside the
    /// base href, and share links are built from it.
    /// </remarks>
    static Task Root(HttpContext context, IdeEndpoint endpoint, string prefix)
    {
        if (context.Request.Path.Value?.EndsWith('/') == true)
        {
            return endpoint.WriteIndex(context);
        }

        var path = context.Request.PathBase + new PathString(prefix) + new PathString("/");
        context.Response.Redirect(path + context.Request.QueryString);
        return Task.CompletedTask;
    }

    /// <summary>Applies a convention to every endpoint one <c>MapBlazorQL</c> call registered.</summary>
    sealed class CompositeConventionBuilder(IReadOnlyList<IEndpointConventionBuilder> builders) :
        IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in builders)
            {
                builder.Add(convention);
            }
        }

        public void Finally(Action<EndpointBuilder> finalConvention)
        {
            foreach (var builder in builders)
            {
                builder.Finally(finalConvention);
            }
        }
    }
}
