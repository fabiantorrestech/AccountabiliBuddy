namespace AccountabiliBuddy.App.Services;

/// <summary>The overlay's order and visibility presets (14c): "Focus first" (the default), "Clock first" and "Just the essentials".</summary>
public static class OverlayContentOrder
{
    public static IReadOnlyList<OverlayItem> FocusFirst { get; } =
    [
        new(OverlayItemKind.Ring, true),
        new(OverlayItemKind.Task, true),
        new(OverlayItemKind.Timeline, true),
        new(OverlayItemKind.Controls, true),
        new(OverlayItemKind.TimeDate, true),
        new(OverlayItemKind.Batteries, true),
        new(OverlayItemKind.Sound, false),
    ];

    public static IReadOnlyList<OverlayItem> ClockFirst { get; } =
    [
        new(OverlayItemKind.TimeDate, true),
        new(OverlayItemKind.Ring, true),
        new(OverlayItemKind.Task, true),
        new(OverlayItemKind.Batteries, true),
        new(OverlayItemKind.Timeline, false),
        new(OverlayItemKind.Controls, false),
        new(OverlayItemKind.Sound, false),
    ];

    public static IReadOnlyList<OverlayItem> Essentials { get; } =
    [
        new(OverlayItemKind.Ring, true),
        new(OverlayItemKind.Controls, true),
        new(OverlayItemKind.Task, false),
        new(OverlayItemKind.Timeline, false),
        new(OverlayItemKind.TimeDate, false),
        new(OverlayItemKind.Batteries, false),
        new(OverlayItemKind.Sound, false),
    ];

    /// <summary>The saved list made whole: every part appears once (parts a saved list lacks are added off at the end).</summary>
    public static IReadOnlyList<OverlayItem> Complete(IReadOnlyList<OverlayItem>? saved)
    {
        if (saved is null || saved.Count == 0)
        {
            return FocusFirst;
        }

        List<OverlayItem> list = saved.GroupBy(item => item.Kind).Select(group => group.First()).ToList();
        foreach (OverlayItemKind kind in Enum.GetValues<OverlayItemKind>().Where(kind => list.All(item => item.Kind != kind)))
        {
            list.Add(new OverlayItem(kind, false));
        }

        return list;
    }
}
