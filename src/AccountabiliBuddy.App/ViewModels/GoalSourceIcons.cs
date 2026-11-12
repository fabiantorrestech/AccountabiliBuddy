using AccountabiliBuddy.Core.Goals;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>The icon of each goal source in Edit goal › Tracking &amp; sources (ADR 32: Material icons, one meaning per icon).</summary>
public static class GoalSourceIcons
{
    public static MaterialIconKind Of(GoalSourceType type) => type switch
    {
        GoalSourceType.FocusAll or GoalSourceType.FocusLabel => MaterialIconKind.TimerOutline,
        GoalSourceType.FocusTag => MaterialIconKind.Pound,
        GoalSourceType.Hand => MaterialIconKind.HandBackRightOutline,
        GoalSourceType.HealthSteps or GoalSourceType.HealthWeight => MaterialIconKind.HeartPulse,
        GoalSourceType.HealthSleep => MaterialIconKind.BedOutline,
        GoalSourceType.ScreenTime => MaterialIconKind.Monitor,
        GoalSourceType.CalendarCategory => MaterialIconKind.CalendarOutline,
        GoalSourceType.Beeminder => MaterialIconKind.Bee,
        GoalSourceType.GoalField => MaterialIconKind.LinkVariant,
        GoalSourceType.MilestoneTasks => MaterialIconKind.FormatListChecks,
        _ => MaterialIconKind.TrackChanges,
    };
}
