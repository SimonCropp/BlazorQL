namespace BlazorQL;

/// <summary>Mounts the BlazorQL IDE in an ASP.NET Core app.</summary>
public static class BlazorQLIdeEndpointRouteBuilderExtensions
{
    /// <summary>Where the IDE mounts when the pattern is left to the package.</summary>
    [StringSyntax("Route")]
    public const string DefaultPattern = "/blazorql";

    /// <summary>
    /// Serves the IDE at <see cref="DefaultPattern"/>, configured. The overload exists so that an
    /// app with something to configure but no reason to move the mount does not have to name the
    /// pattern purely to reach the second parameter.
    /// </summary>
    /// <example>
    /// <code>app.MapBlazorQL(_ =&gt; _.DocumentTitle = "Orders");</code>
    /// </example>
    /// <returns>The mounted endpoints, as <see cref="MapBlazorQL(IEndpointRouteBuilder,string,Action{BlazorQLIdeOptions})"/>.</returns>
    public static IEndpointConventionBuilder MapBlazorQL(
        this IEndpointRouteBuilder endpoints,
        Action<BlazorQLIdeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        return endpoints.MapBlazorQL(DefaultPattern, configure);
    }

    /// <summary>
    /// Serves the IDE at <paramref name="pattern"/>. The whole WebAssembly application is embedded
    /// in this assembly, so there is nothing to deploy alongside it and no Blazor SDK involved.
    /// </summary>
    /// <example>
    /// <code>app.MapBlazorQL("/blazorql", _ => _.Endpoint = "/graphql");</code>
    /// </example>
    /// <returns>
    /// The mounted endpoints, so conventions apply to both of them:
    /// <c>app.MapBlazorQL().RequireAuthorization("Admin")</c>.
    /// </returns>
    public static IEndpointConventionBuilder MapBlazorQL(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern = DefaultPattern,
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
            context => Root(context, endpoint, prefix));

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
    /// <c>/blazorql</c> and <c>/blazorql/</c> and has to tell them apart itself — redirecting
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
