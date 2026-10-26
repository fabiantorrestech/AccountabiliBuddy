using System.Globalization;
using AccountabiliBuddy.App.Services.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>An alarm on Today's Timers card: its time (12-hour), days, on/off, ✎ (name, time, days) and delete.</summary>
public sealed partial class AlarmRowViewModel : ObservableObject
{
    private static readonly DayOfWeek[] Weekdays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private readonly TimersService _timers;
    private bool _refreshing;

    public AlarmRowViewModel(TimersService timers, AlarmItem item)
    {
        _timers = timers;
        Item = item;
        Name = item.Name;
        TimeText = string.Empty;
        DaysText = string.Empty;
        EditName = string.Empty;
        EditDays = DayToggleViewModel.Week();
        Refresh(item);
    }

    public AlarmItem Item { get; private set; }

    public Guid Id => Item.Id;

    [ObservableProperty]
    public partial string Name { get; set; }

    /// <summary>"7:30 AM".</summary>
    [ObservableProperty]
    public partial string TimeText { get; set; }

    /// <summary>"Once", "Every day", "Weekdays", "Mon, Wed, Fri"; plus "· snoozed until 7:39 AM" while snoozed.</summary>
    [ObservableProperty]
    public partial string DaysText { get; set; }

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditName { get; set; }

    [ObservableProperty]
    public partial TimeSpan? EditTime { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DayToggleViewModel> EditDays { get; set; }

    public void Refresh(AlarmItem item)
    {
        Item = item;
        Name = item.Name;
        TimeText = item.Time.ToString("h:mm tt", CultureInfo.InvariantCulture);
        string days = Describe(item.Days);
        DaysText = item.SnoozedUntil is DateTime snoozed
            ? $"{days} · snoozed until {snoozed.ToString("h:mm tt", CultureInfo.InvariantCulture)}"
            : days;
        _refreshing = true;
        IsOn = item.IsOn;
        _refreshing = false;
    }

    partial void OnIsOnChanged(bool value)
    {
        if (!_refreshing)
        {
            _ = _timers.SetAlarmOnAsync(Id, value);
        }
    }

    [RelayCommand]
    private void Edit()
    {
        EditName = Item.Name;
        EditTime = Item.Time.ToTimeSpan();
        EditDays = DayToggleViewModel.Week(Item.Days);
        IsEditing = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsEditing = false;
        if (EditTime is TimeSpan time)
        {
            await _timers.EditAlarmAsync(Id, EditName, TimeOnly.FromTimeSpan(time), EditDays.Where(day => day.IsOn).Select(day => day.Day).ToList());
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void Delete()
    {
        _ = _timers.DeleteAlarmAsync(Id);
    }

    /// <summary>"Once", "Every day", "Weekdays", "Weekends" or "Mon, Wed, Fri" (Monday first).</summary>
    public static string Describe(IReadOnlyList<DayOfWeek> days)
    {
        var set = new HashSet<DayOfWeek>(days);
        if (set.Count == 0)
        {
            return "Once";
        }

        if (set.Count == 7)
        {
            return "Every day";
        }

        if (set.SetEquals(Weekdays))
        {
            return "Weekdays";
        }

        if (set.SetEquals([DayOfWeek.Saturday, DayOfWeek.Sunday]))
        {
            return "Weekends";
        }

        return string.Join(", ", DayToggleViewModel.Week(set).Where(day => day.IsOn).Select(day => day.Label));
    }
}
