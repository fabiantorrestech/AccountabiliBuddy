using AccountabiliBuddy.App.ViewModels;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;
using Microsoft.Data.Sqlite;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The start-of-day assistant (ADR 47 follow-up): on the first arrival of a workday, a card in front of everything asks
/// how many hours of work the user hopes to get done today, what is first (up to 3 tasks), and starts the session (the
/// remembered length, counting down, planning first). <see cref="StartNudgeService"/> decides when it opens; this class
/// remembers that it came today, builds the card and applies its answers with the rules the rest of the app has: raising
/// today's goal or budget applies at once, lowering it waits like any looser change. Use from the UI thread only.
/// </summary>
public sealed class StartOfDayAssistant
{
    /// <summary>The goal card's range (30 minutes to 8 hours).</summary>
    public const int MinMinutes = 30;
    public const int MaxMinutes = 480;
    private const string DocumentKey = "startOfDay";

    private readonly SqliteJsonDocumentStore _store;
    private readonly GoalService _goal;
    private readonly ConfigurationService _configuration;
    private readonly SessionTasksService _tasks;
    private readonly QuickSessionService _quickSession;
    private readonly SessionController _sessions;

    public StartOfDayAssistant(
        SqliteJsonDocumentStore store, GoalService goal, ConfigurationService configuration,
        SessionTasksService tasks, QuickSessionService quickSession, SessionController sessions)
    {
        _store = store;
        _goal = goal;
        _configuration = configuration;
        _tasks = tasks;
        _quickSession = quickSession;
        _sessions = sessions;
    }

    /// <summary>True while lowering the goal has to wait (work left today under the work-hours guard). Set by the app.</summary>
    public Func<bool> GoalLowerWaits { get; set; } = () => false;

    /// <summary>Read today's progress again after the goal changed. Set by the app.</summary>
    public Action RefreshProgress { get; set; } = () => { };

    /// <summary>The last date the assistant came, or a real session ran; null = never.</summary>
    public DateOnly? ShownOn { get; private set; }

    public void LoadBlocking()
    {
        try
        {
            ShownOn = Task.Run(() => _store.LoadAsync<StartOfDayState>(DocumentKey)).GetAwaiter().GetResult()?.ShownOn;
        }
        catch (Exception ex) when (ex is SqliteException or System.Text.Json.JsonException)
        {
            ShownOn = null;
        }
    }

    /// <summary>The assistant already came on <paramref name="date"/> (or a session ran), so the next arrival is the ordinary nudge.</summary>
    public bool IsDoneOn(DateOnly date) => ShownOn == date;

    /// <summary>Remembers that the assistant came on <paramref name="date"/>; writes only when that is news.</summary>
    public void MarkDone(DateOnly date)
    {
        if (ShownOn == date)
        {
            return;
        }

        ShownOn = date;
        _ = SaveAsync();
    }

    /// <summary>The card, with today's hours (Budget or goal) and the tasks of the next session.</summary>
    public StartOfDayCardViewModel CreateCard(
        bool comesBack, Func<StartOfDayCardViewModel, Task<bool>> onStart, Action<StartOfDayCardViewModel, string> onNotNow)
    {
        return new StartOfDayCardViewModel(
            HoursToday(), new ChecklistViewModel(_tasks, ChecklistMode.Draft, _sessions.TimeProvider), _quickSession.Preferences.FocusMinutes,
            comesBack, SetHoursAsync, () => _tasks.Current.Tasks.Count(task => !task.IsDone), onStart, onNotNow);
    }

    /// <summary>Starts the session: the remembered length counting down; planning comes first once it runs.</summary>
    public Task<bool> StartAsync()
    {
        return _quickSession.StartWithDefaultAsync();
    }

    // Today's Budget on a Budget day, else the daily goal; in hours, steps of half an hour.
    private decimal HoursToday()
    {
        DayTemplate template = _configuration.Current.DayPlan.TemplateFor(Today());
        int minutes = template.Mode == DayMode.Budget && template.BudgetMinutes is int budget ? budget : _goal.Current.DailyMinutes;
        return Math.Clamp(Math.Round(minutes / 30m, MidpointRounding.AwayFromZero) * 0.5m, MinMinutes / 60m, MaxMinutes / 60m);
    }

    /// <summary>
    /// Sets today's goal, and on a Budget day today's budget too (as a one-off day for today, so the other days of its type
    /// keep theirs). Raising applies at once; lowering waits like any looser change: the goal stays while work is left under
    /// the work-hours guard, a lower budget is held by the day plan.
    /// </summary>
    /// <returns>What the user should know (what is waiting and why), or null when everything applied.</returns>
    private async Task<string?> SetHoursAsync(decimal hours)
    {
        int minutes = (int)Math.Clamp(Math.Round(hours * 2, MidpointRounding.AwayFromZero) * 30, MinMinutes, MaxMinutes);
        var notes = new List<string>();
        if (minutes != _goal.Current.DailyMinutes)
        {
            if (minutes < _goal.Current.DailyMinutes && GoalLowerWaits())
            {
                notes.Add($"Lowering the goal waits until today's work hours are over, so it stays at {HoursText(_goal.Current.DailyMinutes)}.");
            }
            else
            {
                await _goal.SetAsync(minutes);
                RefreshProgress();
            }
        }

        DateOnly today = Today();
        if (_configuration.Current.DayPlan.TemplateFor(today) is { Mode: DayMode.Budget } template && template.BudgetMinutes != minutes)
        {
            await _configuration.UpdateAsync(document => document with { DayPlan = WithTodaysBudget(document.DayPlan, today, minutes) });
            if (_configuration.Current.DayPlan.TemplateFor(today).BudgetMinutes != minutes)
            {
                string stays = $"Today's budget stays at {HoursText(template.BudgetMinutes ?? 0)} for now.";
                notes.Add(_configuration.DayPlanWaitText is string wait ? $"{stays} {wait}" : $"{stays} Lowering it waits like any looser change.");
            }
        }

        return notes.Count == 0 ? null : string.Join(" ", notes);
    }

    private static DayPlan WithTodaysBudget(DayPlan plan, DateOnly today, int minutes)
    {
        DayTemplate changed = plan.TemplateFor(today) with { BudgetMinutes = minutes };
        IReadOnlyList<OneOffDay> days = plan.OneOffDays.Any(day => day.Date == today)
            ? plan.OneOffDays.Select(day => day.Date == today ? day with { Template = changed } : day).ToList()
            : [.. plan.OneOffDays, new OneOffDay(Guid.NewGuid(), today, changed, "Budget set by the start-of-day assistant")];
        return plan with { OneOffDays = days };
    }

    private static string HoursText(int minutes)
    {
        return minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_sessions.Now, _sessions.TimeProvider.LocalTimeZone).DateTime);

    private async Task SaveAsync()
    {
        try
        {
            await _store.SaveAsync(DocumentKey, new StartOfDayState(ShownOn));
        }
        catch (SqliteException)
        {
            // Once a day is a convenience; the worst case is the card coming once more after a restart.
        }
    }
}
