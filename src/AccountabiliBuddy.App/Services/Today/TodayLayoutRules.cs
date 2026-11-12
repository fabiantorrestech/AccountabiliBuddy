namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// The pure rules of the Today layout (Settings › Today): reading a stored layout safely, the guard rule, moving and placing parts.
/// No UI and no storage, so the checks can call it.
/// </summary>
public static class TodayLayoutRules
{
    /// <summary>
    /// Every part once, in drawing order: the stored order first (unknown keys and repeats dropped), then any part the stored layout
    /// doesn't name (added in a later version) at the end in its default place. Null gives the defaults.
    /// </summary>
    public static IReadOnlyList<TodayPartChoice> Resolve(IReadOnlyList<TodayPartChoice>? stored)
    {
        var result = new List<TodayPartChoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TodayPartChoice choice in stored ?? [])
        {
            if (TodayParts.ByKey(choice.Key) is not null && Enum.IsDefined(choice.Placement) && seen.Add(choice.Key))
            {
                result.Add(choice);
            }
        }

        foreach (TodayPartInfo info in TodayParts.All.Where(info => !seen.Contains(info.Key)))
        {
            result.Add(new TodayPartChoice(info.Key, info.DefaultPlacement));
        }

        return result;
    }

    /// <summary>
    /// Where a part is drawn: its chosen place, except that outside ProductaBuddy a part that carries a guard
    /// (<see cref="TodayPartInfo.ShownWhileGuarded"/>, the lunch chip) is never off; a stored Off draws it as a chip.
    /// </summary>
    public static TodayPartPlacement Effective(TodayPartInfo part, TodayPartPlacement chosen, bool isProductaBuddy)
    {
        return chosen == TodayPartPlacement.Off && !CanTurnOff(part, isProductaBuddy) ? TodayPartPlacement.Chip : chosen;
    }

    /// <summary>Off is offered for the part: not for the lunch chip outside ProductaBuddy.</summary>
    public static bool CanTurnOff(TodayPartInfo part, bool isProductaBuddy) => isProductaBuddy || !part.ShownWhileGuarded;

    /// <summary>
    /// The layout with <paramref name="key"/> at <paramref name="placement"/>; unchanged for an unknown key or an Off the part may not take.
    /// </summary>
    public static IReadOnlyList<TodayPartChoice> WithPlacement(
        IReadOnlyList<TodayPartChoice> parts, string key, TodayPartPlacement placement, bool isProductaBuddy)
    {
        if (TodayParts.ByKey(key) is not { } info || (placement == TodayPartPlacement.Off && !CanTurnOff(info, isProductaBuddy)))
        {
            return parts;
        }

        return parts.Select(choice => choice.Key == key ? choice with { Placement = placement } : choice).ToList();
    }

    /// <summary>The layout with <paramref name="key"/> moved to <paramref name="index"/> (clamped); unchanged for an unknown key.</summary>
    public static IReadOnlyList<TodayPartChoice> Move(IReadOnlyList<TodayPartChoice> parts, string key, int index)
    {
        int from = parts.ToList().FindIndex(choice => choice.Key == key);
        if (from < 0)
        {
            return parts;
        }

        var next = parts.ToList();
        TodayPartChoice moving = next[from];
        next.RemoveAt(from);
        next.Insert(Math.Clamp(index, 0, next.Count), moving);
        return next;
    }
}
