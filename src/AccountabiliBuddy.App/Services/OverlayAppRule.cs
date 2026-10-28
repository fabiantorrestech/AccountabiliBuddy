namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the overlay does while one app is in front (matched by process name, without ".exe", ignoring case). The built-in list is
/// <see cref="OverlayAppRules.Defaults"/>; the user's edited list replaces it whole.
/// </summary>
/// <param name="OffsetX">Sideways shift in px at 100% scaling (positive = right); floating tiles and the Large size only.</param>
/// <param name="OffsetY">Vertical shift (negative = up); floating tiles and the Large size only: Word's status bar sits there.</param>
/// <param name="HideDuringCalls">Hidden while the app's call window is in front (<paramref name="CallClasses"/>), or while it is full screen.</param>
/// <param name="CallClasses">Window class names of the app's call windows. Class names only: no window title is read.</param>
/// <param name="TimeOnly">The corner clock shows the time only, more transparent (games).</param>
public sealed record OverlayAppRule(
    string Process,
    int OffsetX = 0,
    int OffsetY = 0,
    bool HideDuringCalls = false,
    IReadOnlyList<string>? CallClasses = null,
    bool TimeOnly = false);
