using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Timers;
using Avalonia.Media;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The on-screen window's timers and alarms: a line per running or paused timer (and one that's up until dismissed),
/// then the alarms ringing within the next 12 hours. Updated in place each tick so the bars glide. Quiet at rest like
/// the rest of the window (slate); a timer that's up stays amber.
/// </summary>
public sealed class OverlayTimersViewModel
{
    private static readonly TimeSpan AlarmsAhead = TimeSpan.FromHours(12);
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
    private static readonly IBrush PausedBrush = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush UpBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush QuietBrush = new SolidColorBrush(SessionOverlayViewModel.QuietColor);

    private readonly TimersService _timers;

    public OverlayTimersViewModel(TimersService timers)
    {
        _timers = timers;
    }

    public ObservableCollection<OverlayTimerLine> Lines { get; } = [];

    public bool HasLines => Lines.Count > 0;

    /// <param name="quiet">At rest in quiet mode: the bars are slate.</param>
    /// <param name="fillUp">The bar fills as time passes (Settings → On-screen timer), else it empties.</param>
    public void Refresh(bool quiet, bool fillUp)
    {
        // An unused feature costs nothing per tick.
        if (_timers.Timers.Count == 0 && _timers.Alarms.Count == 0)
        {
            if (Lines.Count > 0)
            {
                Lines.Clear();
            }

            return;
        }

        DateTimeOffset now = _timers.Now;
        DateTime localNow = _timers.LocalNow;

        var shown = new List<(Guid Id, bool IsAlarm, Action<OverlayTimerLine> Fill)>();
        IEnumerable<TimerItem> timers = _timers.Timers
            .Where(timer => timer.State != TimerState.Done || !timer.Dismissed)
            .OrderBy(timer => timer.State switch { TimerState.Done => 0, TimerState.Running => 1, _ => 2 })
            .ThenBy(timer => timer.LeftAt(now));
        foreach (TimerItem timer in timers)
        {
            shown.Add((timer.Id, false, line => FillTimer(line, timer, now, quiet, fillUp)));
        }

        IEnumerable<(AlarmItem Alarm, DateTime Next)> alarms = _timers.Alarms
            .Select(alarm => (Alarm: alarm, Next: alarm.NextRing()))
            .Where(pair => pair.Next is DateTime next && next - localNow <= AlarmsAhead)
            .Select(pair => (pair.Alarm, pair.Next!.Value))
            .OrderBy(pair => pair.Item2);
        foreach ((AlarmItem alarm, DateTime next) in alarms)
        {
            shown.Add((alarm.Id, true, line => FillAlarm(line, alarm, next, quiet)));
        }

        // In place: same lines for the same ids, in the new order.
        for (int index = 0; index < shown.Count; index++)
        {
            (Guid id, bool isAlarm, Action<OverlayTimerLine> fill) = shown[index];
            int existing = IndexOf(id, index);
            if (existing < 0)
            {
                Lines.Insert(index, new OverlayTimerLine(_timers, id, isAlarm));
            }
            else if (existing != index)
            {
                Lines.Move(existing, index);
            }

            fill(Lines[index]);
        }

        while (Lines.Count > shown.Count)
        {
            Lines.RemoveAt(Lines.Count - 1);
        }
    }

    private int IndexOf(Guid id, int from)
    {
        for (int index = from; index < Lines.Count; index++)
        {
            if (Lines[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    private static void FillTimer(OverlayTimerLine line, TimerItem timer, DateTimeOffset now, bool quiet, bool fillUp)
    {
        TimeSpan left = timer.LeftAt(now);
        string time = SessionStatusFormatter.FormatDuration(left);
        line.Name = timer.Name;
        line.IsRunning = timer.State == TimerState.Running;
        line.IsUp = timer.State == TimerState.Done;
        line.TimeText = timer.State switch
        {
            TimerState.Running => $"{time} left",
            TimerState.Paused => $"paused · {time}",
            _ => "up",
        };
        line.PauseIcon = line.IsRunning ? MaterialIconKind.Pause : MaterialIconKind.Play;
        line.PauseTip = line.IsRunning ? "Pause" : line.IsUp ? "Start it again" : "Resume";
        line.StopTip = line.IsUp ? "Dismiss" : "Stop this timer";

        // +1 min can take a timer past its length: the bar is then full.
        double whole = Math.Max(timer.Length.TotalSeconds, left.TotalSeconds);
        double remaining = whole <= 0 ? 0 : left.TotalSeconds / whole;
        double shownPart = timer.State == TimerState.Done ? 1 : fillUp ? 1 - remaining : remaining;
        line.BarWidth = Math.Round(shownPart * SessionOverlayViewModel.BarLength * 2) / 2;

        IBrush brush = line.IsUp ? UpBrush : quiet ? QuietBrush : line.IsRunning ? RunningBrush : PausedBrush;
        if (line.BarBrush != brush)
        {
            line.BarBrush = brush;
        }
    }

    private static void FillAlarm(OverlayTimerLine line, AlarmItem alarm, DateTime next, bool quiet)
    {
        line.Name = alarm.Name;
        string at = next.ToString("h:mm tt", CultureInfo.InvariantCulture);
        line.TimeText = alarm.SnoozedUntil is not null ? $"snoozed · {at}" : at;
        line.BarWidth = 0;
        IBrush brush = quiet ? QuietBrush : RunningBrush;
        if (line.BarBrush != brush)
        {
            line.BarBrush = brush;
        }
    }
}
