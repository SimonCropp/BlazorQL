namespace BlazorQL;

/// <summary>
/// Holds the theme preference and resolves it to a concrete light/dark mode. Persistence arrives
/// in M6 — for now the preference lives for the component's lifetime.
/// </summary>
public sealed class ThemeService
{
    public Theme Current { get; set; } = Theme.System;

    /// <summary>Advances System → Light → Dark → System and returns the new preference.</summary>
    public Theme Cycle() =>
        Current = Current switch
        {
            Theme.System => Theme.Light,
            Theme.Light => Theme.Dark,
            _ => Theme.System
        };

    /// <summary>The concrete mode, given what the OS prefers.</summary>
    public string Resolve(bool systemDark)
    {
        if (Current == Theme.Dark || (Current == Theme.System && systemDark))
        {
            return "dark";
        }

        return "light";
    }
}
