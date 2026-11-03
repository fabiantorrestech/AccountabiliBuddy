using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Today's quiet "from calendar" lines (ADR 58): which parts of today's guarded windows calendar events made, and the Budget
/// credit they gave. Empty, and hidden, when no event changed anything. A hidden calendar's events count, as they do for the
/// guards. It reads again when the plan, the calendar or the choices change, and when the day bar's status line moves on.
/// </summary>
public sealed partial class CalendarTodayViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly EffectiveDayPlanProvider _overlay;
    private readonly TimeProvider _time;
    private readonly DayBarViewModel? _day;

    /// <param name="day">Today's bar, for the focus and night inputs a Budget day needs and as a clock; null reads without them.</param>
    public CalendarTodayViewModel(
        ConfigurationService configuration, EffectiveDayPlanProvider overlay, CalendarService calendars, TimeProvider time, DayBarViewModel? day)
    {
        _configuration = configuration;
        _overlay = overlay;
        _time = time;
        _day = day;
        Lines = [];
        Refresh();
        _configuration.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        calendars.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        // The store can raise this on any thread and inside a lock: post to the UI thread, never read the provider here.
        _overlay.Store.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        if (_day is not null)
        {
            _day.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DayBarViewModel.StatusText))
                {
                    Refresh();
                }
            };
        }
    }

    [ObservableProperty]
    public partial IReadOnlyList<string> Lines { get; set; }

    [ObservableProperty]
    public partial bool HasLines { get; set; }

    private void Refresh()
    {
        DateTime now = _time.GetLocalNow().DateTime;
        IReadOnlyList<string> lines = CalendarTodayText.Lines(
            _overlay.Current, _configuration.Current.DayPlan, DateOnly.FromDateTime(now), now, _day?.FocusedOn, _day?.NightFallback);

        // Assigned only when the text differs, so the list isn't rebuilt for nothing.
        if (!lines.SequenceEqual(Lines))
        {
            Lines = lines;
        }

        HasLines = Lines.Count > 0;
    }
}
