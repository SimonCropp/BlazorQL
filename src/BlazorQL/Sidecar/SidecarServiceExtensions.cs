namespace BlazorQL;

/// <summary>Registration for the debug sidecar.</summary>
public static class SidecarServiceExtensions
{
    // begin-snippet: sidecarRegistration
    /// <summary>
    /// Registers the sidecar's store and options. Two things remain for the app: wrap its fetcher
    /// (<c>new SidecarFetcher(fetcher, store)</c>) so requests are captured, and render
    /// <see cref="BlazorQLSidecar"/> once on the page.
    /// </summary>
    /// <remarks>
    /// The store is scoped. It holds every captured request's headers, authorization included, and
    /// every response, so it has to belong to one user: under Blazor Server each circuit is a scope
    /// of its own, where a singleton would show every user everyone's traffic. A WebAssembly app has
    /// the one scope, so there it is the one store either way. The wrapped fetcher is registered
    /// scoped to match — a singleton would capture the root provider's store, which no panel reads.
    /// </remarks>
    public static IServiceCollection AddBlazorQLSidecar(
        this IServiceCollection services,
        Action<SidecarOptions>? configure = null)
    {
        var options = new SidecarOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped<SidecarStore>();
        return services;
    }
    // end-snippet
}
