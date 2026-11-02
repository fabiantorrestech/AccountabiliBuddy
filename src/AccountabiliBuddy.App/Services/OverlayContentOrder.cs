namespace AccountabiliBuddy.App.Services;

/// <summary>The overlay's order and visibility presets (14c): "Focus first" (the default), "Clock first" and "Just the essentials".</summary>
public static class OverlayContentOrder
{
    public static IReadOnlyList<OverlayItem> FocusFirst { get; } =
    [
        new(OverlayItemKind.Ring, true),
        new(OverlayItemKind.Minutes, true),
        new(OverlayItemKind.Task, true),
        new(OverlayItemKind.Controls, true),
        new(OverlayItemKind.TimeDate, true),
        new(OverlayItemKind.Batteries, true),
        new(OverlayItemKind.Sound, false),
    ];

    public static IReadOnlyList<OverlayItem> ClockFirst { get; } =
    [
        new(OverlayItemKind.TimeDate, true),
        new(OverlayItemKind.Ring, true),
        new(OverlayItemKind.Minutes, true),
        new(OverlayItemKind.Task, true),
        new(OverlayItemKind.Batteries, true),
        new(OverlayItemKind.Controls, false),
        new(OverlayItemKind.Sound, false),
    ];

    public static IReadOnlyList<OverlayItem> Essentials { get; } =
    [
        new(OverlayItemKind.Ring, true),
        new(OverlayItemKind.Minutes, true),
        new(OverlayItemKind.Controls, true),
        new(OverlayItemKind.Task, false),
        new(OverlayItemKind.TimeDate, false),
        new(OverlayItemKind.Batteries, false),
        new(OverlayItemKind.Sound, false),
    ];

    /// <summary>
    /// What an untouched list (never saved) shows: the clock and batteries are on where the tile always drew them (Wide, and over the tray) and
    /// off at Small, Medium and Large, which kept the tile as it was before those two could be switched on there.
    /// </summary>
    public static IReadOnlyList<OverlayItem> DefaultFor(OverlaySize size, OverlayPlacement placement)
    {
        return size == OverlaySize.Wide || placement == OverlayPlacement.CoverTray ? FocusFirst : FocusFirstWithoutClock;
    }

    private static IReadOnlyList<OverlayItem> FocusFirstWithoutClock { get; } =
        FocusFirst.Select(item => item.Kind is OverlayItemKind.TimeDate or OverlayItemKind.Batteries ? item with { Enabled = false } : item).ToList();

    /// <summary>
    /// The saved list made whole: every part appears once (parts a saved list lacks are added off at the end), and the retired Timeline row never appears.
    /// A list saved before Minutes existed is moved over first (see <see cref="Migrated"/>); nothing is rewritten on disk until the user changes the list.
    /// </summary>
    public static IReadOnlyList<OverlayItem> Complete(IReadOnlyList<OverlayItem>? saved, OverlayProgress progress)
    {
        if (saved is null || saved.Count == 0)
        {
            return FocusFirst;
        }

        IReadOnlyList<OverlayItem> source = saved.Any(item => item.Kind == OverlayItemKind.Minutes) ? saved : Migrated(saved, progress);
        List<OverlayItem> list = source.Where(item => item.Kind != OverlayItemKind.Timeline).GroupBy(item => item.Kind).Select(group => group.First()).ToList();
        foreach (OverlayItemKind kind in Enum.GetValues<OverlayItemKind>().Where(kind => kind != OverlayItemKind.Timeline && list.All(item => item.Kind != kind)))
        {
            list.Add(new OverlayItem(kind, false));
        }

        return list;
    }

    /// <summary>
    /// A list saved when "Ring and minutes" and "Timeline" were two rows. Minutes takes the old Ring row's switch and place; the one "Ring or
    /// timeline" row sits right before it (where the ring always drew, left of the text) and is on if the old row for the current Progress setting
    /// was on. The old Timeline row only mattered at Wide and Large, where it drew a second progress line; that line is gone.
    /// </summary>
    private static IReadOnlyList<OverlayItem> Migrated(IReadOnlyList<OverlayItem> saved, OverlayProgress progress)
    {
        OverlayItem? oldRing = saved.FirstOrDefault(item => item.Kind == OverlayItemKind.Ring);
        OverlayItem? oldTimeline = saved.FirstOrDefault(item => item.Kind == OverlayItemKind.Timeline);
        bool progressOn = progress == OverlayProgress.Ring ? oldRing?.Enabled ?? false : oldTimeline?.Enabled ?? false;
        var list = new List<OverlayItem>();
        foreach (OverlayItem item in saved)
        {
            if (item.Kind == OverlayItemKind.Ring)
            {
                list.Add(new OverlayItem(OverlayItemKind.Ring, progressOn));
                list.Add(new OverlayItem(OverlayItemKind.Minutes, item.Enabled));
            }
            else
            {
                list.Add(item);
            }
        }

        if (oldRing is null)
        {
            // No Ring row in the list: nothing to take the place from, so both go first.
            list.InsertRange(0, [new OverlayItem(OverlayItemKind.Ring, progressOn), new OverlayItem(OverlayItemKind.Minutes, false)]);
        }

        return list;
    }
}
