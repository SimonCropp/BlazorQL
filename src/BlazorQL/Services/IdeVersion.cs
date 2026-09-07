/// <summary>
/// The version shown beside the logo, read once from the assembly the IDE ships in — which is the
/// RCL in both deliveries, since BlazorQL.Bundled embeds this same component.
/// </summary>
/// <remarks>
/// The informational version rather than <c>AssemblyVersion</c>, which is pinned at 1.0.0 so that
/// a consumer's binding never breaks on a release. SourceLink appends "+{commit}" to it, and a
/// header has no use for the commit.
/// </remarks>
static class IdeVersion
{
    public static readonly string Current = Read();

    static string Read()
    {
        var informational = typeof(BlazorQLIde).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (informational is not {Length: > 0})
        {
            return "";
        }

        var commit = informational.IndexOf('+');
        return commit < 0 ? informational : informational[..commit];
    }
}
