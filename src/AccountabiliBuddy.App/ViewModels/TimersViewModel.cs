using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's Timers card (this PC only, V1): start a timer from 5/10/15/25 min or any length, with an optional name;
/// add an alarm (time, days); and the list of both, each editable. Works during a focus session too; nothing here
/// touches the session, enforcement or History.
/// </summary>
public sealed partial class TimersViewModel : ObservableObject
{
    private readonly TimersService _timers;

    public TimersViewModel(TimersService timers, SessionController sessions)
    {
        _timers = timers;
        NewTimerName = string.Empty;
        NewAlarmName = string.Empty;
        CustomMinutes = 45;
        NewAlarmTime = new TimeSpan(7, 0, 0);
        NewAlarmDays = DayToggleViewModel.Week();
        _timers.Changed += (_, _) => Refresh();
        sessions.Ticked += (_, _) =>
        {
            foreach (TimerRowViewModel row in Timers)
            {
                row.Tick();
            }
        };
        Refresh();
    }

    public ObservableCollection<TimerRowViewModel> Timers { get; } = [];

    public ObservableCollection<AlarmRowViewModel> Alarms { get; } = [];

    [ObservableProperty]
    public partial bool HasTimers { get; set; }

    [ObservableProperty]
    public partial bool HasAlarms { get; set; }

    /// <summary>The next timer's name; blank = "Timer".</summary>
    [ObservableProperty]
    public partial string NewTimerName { get; set; }

    [ObservableProperty]
    public partial decimal? CustomMinutes { get; set; }

    /// <summary>The alarm form, folded until "Add alarm".</summary>
    [ObservableProperty]
    public partial bool IsAddingAlarm { get; set; }

    [ObservableProperty]
    public partial string NewAlarmName { get; set; }

    [ObservableProperty]
    public partial TimeSpan? NewAlarmTime { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<DayToggleViewModel> NewAlarmDays { get; set; }

    /// <summary>A preset button: its minutes as the parameter ("5", "10", …).</summary>
    [RelayCommand]
    private async Task StartPresetAsync(string minutes)
    {
        if (int.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
        {
            await StartAsync(TimeSpan.FromMinutes(value));
        }
    }

    [RelayCommand]
    private async Task StartCustomAsync()
    {
        if (CustomMinutes is decimal minutes && minutes > 0)
        {
            await StartAsync(TimeSpan.FromMinutes((double)minutes));
        }
    }

    [RelayCommand]
    private void ToggleAddAlarm()
    {
        IsAddingAlarm = !IsAddingAlarm;
    }

    [RelayCommand]
    private async Task AddAlarmAsync()
    {
        if (NewAlarmTime is not TimeSpan time)
        {
            return;
        }

        List<DayOfWeek> days = NewAlarmDays.Where(day => day.IsOn).Select(day => day.Day).ToList();
        string name = NewAlarmName;
        IsAddingAlarm = false;
        NewAlarmName = string.Empty;
        NewAlarmDays = DayToggleViewModel.Week();
        await _timers.AddAlarmAsync(name, TimeOnly.FromTimeSpan(time), days);
    }

    private Task StartAsync(TimeSpan length)
    {
        string name = NewTimerName;
        NewTimerName = string.Empty;
        return _timers.AddTimerAsync(name, length);
    }

    // In place by id, so a row being edited keeps its form when another timer changes.
    private void Refresh()
    {
        Sync(Timers, _timers.Timers, item => item.Id, row => row.Id, item => new TimerRowViewModel(_timers, item), (row, item) => row.Refresh(item));
        Sync(Alarms, _timers.Alarms, item => item.Id, row => row.Id, item => new AlarmRowViewModel(_timers, item), (row, item) => row.Refresh(item));
        HasTimers = Timers.Count > 0;
        HasAlarms = Alarms.Count > 0;
    }

    private static void Sync<TItem, TRow>(
        ObservableCollection<TRow> rows,
        IReadOnlyList<TItem> items,
        Func<TItem, Guid> itemId,
        Func<TRow, Guid> rowId,
        Func<TItem, TRow> create,
        Action<TRow, TItem> update)
    {
        var wanted = items.Select(itemId).ToHashSet();
        for (int index = rows.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(rowId(rows[index])))
            {
                rows.RemoveAt(index);
            }
        }

        for (int index = 0; index < items.Count; index++)
        {
            TItem item = items[index];
            int existing = -1;
            for (int search = index; search < rows.Count; search++)
            {
                if (rowId(rows[search]) == itemId(item))
                {
                    existing = search;
                    break;
                }
            }

            if (existing < 0)
            {
                rows.Insert(index, create(item));
                continue;
            }

            if (existing != index)
            {
                rows.Move(existing, index);
            }

            update(rows[index], item);
        }
    }
}
