using Microsoft.Extensions.DependencyInjection;

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
    public static IServiceCollection AddBlazorQLSidecar(
        this IServiceCollection services,
        Action<SidecarOptions>? configure = null)
    {
        var options = new SidecarOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<SidecarStore>();
        return services;
    }
    // end-snippet
}
