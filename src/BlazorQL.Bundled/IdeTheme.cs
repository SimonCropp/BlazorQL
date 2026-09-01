namespace BlazorQL;

/// <summary>The IDE's theme preference. System defers to the OS via prefers-color-scheme.</summary>
/// <remarks>
/// Declared here rather than reusing BlazorQL's own Theme so that this assembly references nothing:
/// a single dependency-free dll is the whole point of the package.
/// </remarks>
public enum IdeTheme
{
    System,
    Light,
    Dark
}
