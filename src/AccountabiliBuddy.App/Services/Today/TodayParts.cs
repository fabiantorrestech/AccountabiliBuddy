using Material.Icons;

namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// The Today sidebar's parts in their default order (board PC-Settings-Today): Next up, Today's goals and Come back to as panels;
/// Timers, Lunch and Beeminder as chips; Calendar changes off. Daily steps (not on the board) is a chip, shown only once a phone shares
/// steps, so the old steps card is not lost. The board's "Phone-free streak" has no PC data yet and
/// is not listed.
/// </summary>
public static class TodayParts
{
    public static IReadOnlyList<TodayPartInfo> All { get; } =
    [
        new(TodayPart.NextUp, "next-up", "Next up", "A small day bar and the next three parts of today", MaterialIconKind.ViewAgendaOutline, TodayPartPlacement.Panel),
        // Round 2: today's deep work against the daily goal until the Goals page (R5) brings the goals list.
        new(TodayPart.Goals, "goals", "Today's goals", "Ticks and logs for goals shown on Today", MaterialIconKind.Bullseye, TodayPartPlacement.Panel),
        new(TodayPart.Comebacks, "comebacks", "Come back to", "Things you parked with Ctrl+Alt+N", MaterialIconKind.FlagOutline, TodayPartPlacement.Panel),
        new(TodayPart.Timers, "timers", "Timers", "The nearest timer, with its controls", MaterialIconKind.TimerOutline, TodayPartPlacement.Chip),
        new(TodayPart.Lunch, "lunch-chip", "Lunch", "Start or skip lunch", MaterialIconKind.SilverwareForkKnife, TodayPartPlacement.Chip, ShownWhileGuarded: true),
        new(TodayPart.Beeminder, "beeminder", "Beeminder", "Read-only goals with their safe days", MaterialIconKind.Bee, TodayPartPlacement.Chip),
        new(TodayPart.CalendarChanges, "calendar-changes", "Calendar changes", "Events added or moved today", MaterialIconKind.CalendarMonthOutline, TodayPartPlacement.Off),
        new(TodayPart.DailySteps, "daily-steps", "Daily steps", "Steps your phone read today, against the goal", MaterialIconKind.Walk, TodayPartPlacement.Chip),
    ];

    public static TodayPartInfo Of(TodayPart part) => All.First(info => info.Part == part);

    public static TodayPartInfo? ByKey(string key) => All.FirstOrDefault(info => info.Key == key);
}
