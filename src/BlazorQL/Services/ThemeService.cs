namespace BlazorQL;

/// <summary>The IDE's theme preference. System defers to the OS via prefers-color-scheme.</summary>
public enum Theme
{
    System,
    Light,
    Dark
}

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
    public string Resolve(bool systemDark) =>
        Current == Theme.Dark || (Current == Theme.System && systemDark)
            ? "dark"
            : "light";
}
