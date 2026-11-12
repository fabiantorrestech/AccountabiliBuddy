using Material.Icons;

namespace AccountabiliBuddy.App.Services.Today;

/// <summary>One arrangeable part of the Today sidebar (<see cref="TodayParts.All"/>).</summary>
/// <param name="Key">
/// Stored in "today.layout". Where the phone's Today has the same thing, the phone's key (<c>TodayItem.key</c>): comebacks, timers,
/// lunch-chip, beeminder, daily-steps.
/// </param>
/// <param name="Title">The row's name in Settings › Today and the panel's heading.</param>
/// <param name="About">The row's muted second line in Settings › Today.</param>
/// <param name="Icon">The row's and the chip's icon (ADR 32: the same meaning has the same icon everywhere).</param>
/// <param name="DefaultPlacement">Where it sits while nothing was chosen (the board's choice).</param>
/// <param name="ShownWhileGuarded">
/// Outside ProductaBuddy it can never be off (brief §4.2: the lunch chip always shows while a guard is on): a stored Off draws it as a
/// chip, and Settings refuses Off.
/// </param>
public sealed record TodayPartInfo(
    TodayPart Part,
    string Key,
    string Title,
    string About,
    MaterialIconKind Icon,
    TodayPartPlacement DefaultPlacement,
    bool ShownWhileGuarded = false);
