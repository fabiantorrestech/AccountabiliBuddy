using AccountabiliBuddy.App.Services.Today;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One part of the Today sidebar as drawn now: a panel or a chip (<see cref="TodaySidebarViewModel"/>). The panel's content comes
/// from the sidebar's own view models (<see cref="Owner"/>); <see cref="IsAvailable"/> hides a part with nothing to show (Beeminder
/// without a token, no lunch today, no calendar changes, no steps from a phone).
/// </summary>
public sealed partial class TodaySidebarPartViewModel : ObservableObject
{
    public TodaySidebarPartViewModel(TodaySidebarViewModel owner, TodayPartInfo info, TodayPartPlacement placement)
    {
        Owner = owner;
        Info = info;
        Placement = placement;
    }

    public TodaySidebarViewModel Owner { get; }

    public TodayPartInfo Info { get; }

    public TodayPartPlacement Placement { get; }

    public TodayPart Part => Info.Part;

    public string Title => Info.Title;

    public MaterialIconKind Icon => Info.Icon;

    public bool IsNextUp => Part == TodayPart.NextUp;

    public bool IsGoals => Part == TodayPart.Goals;

    public bool IsComebacks => Part == TodayPart.Comebacks;

    public bool IsTimers => Part == TodayPart.Timers;

    public bool IsLunch => Part == TodayPart.Lunch;

    public bool IsBeeminder => Part == TodayPart.Beeminder;

    public bool IsCalendarChanges => Part == TodayPart.CalendarChanges;

    /// <summary>It has something to show now.</summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    /// <summary>A chip's count after its name ("Timers 1"); empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChipCount))]
    public partial string ChipCount { get; set; } = string.Empty;

    public bool HasChipCount => ChipCount.Length > 0;
}
