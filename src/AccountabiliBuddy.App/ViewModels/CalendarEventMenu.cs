using System.Windows.Input;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The menu on a calendar event (ADR 64), the same wherever the event shows: what it counts as now, Classify as (the whole series on a repeating event, or just this
/// one), Don't classify, Back to automatic, Always classify like this, and Add a task for it. It works through the event picker's choices and wording, so a looser
/// change waits and says so exactly as there. During a session the user started the classify entries are shut; a task can still be added.
/// </summary>
public sealed class CalendarEventMenu
{
    private readonly EffectiveDayPlanProvider _provider;
    private readonly ConfigurationService _configuration;
    private readonly CalendarService _calendars;
    private readonly CalendarEventActions _actions;
    private readonly TimeProvider _time;
    private readonly Action<string> _notify;

    /// <param name="notify">Shows one line saying what a choice did.</param>
    public CalendarEventMenu(
        EffectiveDayPlanProvider provider, ConfigurationService configuration, CalendarService calendars, CalendarEventActions actions, TimeProvider time, Action<string> notify)
    {
        _provider = provider;
        _configuration = configuration;
        _calendars = calendars;
        _actions = actions;
        _time = time;
        _notify = notify;
    }

    /// <summary>The categories an event can be given: Sleep and Pre-Work never (ADR 58).</summary>
    public IReadOnlyList<DayCategory> Categories =>
        _configuration.Current.DayPlan.Categories.Where(category => category.Behavior is not (CategoryBehavior.Sleep or CategoryBehavior.PreWork)).ToList();

    /// <summary>The suggested keyword for "Always classify like this".</summary>
    public string SuggestKeyword(CalendarEvent calendarEvent) => EventKeywordRules.SuggestKeyword(calendarEvent.Title);

    /// <summary>Saves a keyword rule and says what happened.</summary>
    public void SaveKeywordRule(CalendarEvent calendarEvent, string keyword, Guid categoryId)
    {
        _notify(_actions.TrySaveKeywordRule(calendarEvent, keyword, categoryId)
            ? $"Saved. Events with \"{keyword.Trim()}\" in the title follow it after {(int)_provider.LooseningDelay.TotalMinutes} min."
            : "Not changed: a focus session you started is running.");
    }

    /// <param name="askKeywordRule">Opens the "Always classify like this" dialog (the view knows the window).</param>
    public IReadOnlyList<TaskMenuEntry> EntriesFor(CalendarEvent calendarEvent, Action askKeywordRule)
    {
        CalendarEventPickerViewModel series = Picker(calendarEvent, wholeSeries: true);
        var entries = new List<TaskMenuEntry>
        {
            new(calendarEvent.Title, IsHeading: true),
            new(calendarEvent.IsAllDay ? "All day · Calendar" : $"{series.WhenText} · {series.NowText}", IsHeading: true),
            TaskMenuEntry.Separator,
        };

        if (series.CanClassify && series.IsLocked)
        {
            entries.Add(new TaskMenuEntry("Locked during a focus session", Icon: MaterialIconKind.LockOutline));
            entries.Add(TaskMenuEntry.Separator);
        }
        else if (series.CanClassify)
        {
            var classify = new List<TaskMenuEntry>(CategoryEntries(series));
            if (series.IsSeries)
            {
                classify.Add(TaskMenuEntry.Separator);
                classify.Add(TaskMenuEntry.Group("Just this one", CategoryEntries(Picker(calendarEvent, wholeSeries: false))));
            }

            entries.Add(TaskMenuEntry.Group("Classify as", classify, MaterialIconKind.TagOutline));
            entries.AddRange(series.Choices.Where(choice => choice.Kind != EventChoiceKind.Category).Select(choice => ChoiceEntry(series, choice)));
            if (_actions.CanMakeKeywordRule(calendarEvent))
            {
                entries.Add(TaskMenuEntry.Do("Always classify like this…", askKeywordRule, MaterialIconKind.AutoFix));
            }

            entries.Add(TaskMenuEntry.Separator);
        }

        entries.Add(TaskMenuEntry.Async("Add a task for it", () => AddTaskAsync(calendarEvent), MaterialIconKind.PlaylistPlus));
        return entries;
    }

    private IReadOnlyList<TaskMenuEntry> CategoryEntries(CalendarEventPickerViewModel picker)
    {
        return picker.Choices.Where(choice => choice.Kind == EventChoiceKind.Category).Select(choice => ChoiceEntry(picker, choice)).ToList();
    }

    private TaskMenuEntry ChoiceEntry(CalendarEventPickerViewModel picker, EventCategoryChoiceViewModel choice)
    {
        ICommand command = new RelayCommand(() =>
        {
            choice.ChooseCommand.Execute(null);
            if (picker.Message is string message)
            {
                _notify(message);
            }
        });
        string text = choice.IsWaiting ? $"{choice.Text} ({choice.WaitText})" : choice.Text;
        return new TaskMenuEntry(text, command, IsChecked: choice.IsCurrent, Dot: choice.Brush);
    }

    private CalendarEventPickerViewModel Picker(CalendarEvent calendarEvent, bool wholeSeries)
    {
        string calendar = _calendars.Sources.SelectMany(source => source.Calendars).FirstOrDefault(item => item.Id == calendarEvent.CalendarId)?.Name ?? string.Empty;
        return new CalendarEventPickerViewModel(_provider, _configuration, _time, calendarEvent, calendar, () => { }, () => { })
        {
            IsWholeSeries = wholeSeries,
        };
    }

    private async Task AddTaskAsync(CalendarEvent calendarEvent)
    {
        _notify(await _actions.AddTaskAsync(calendarEvent)
            ? $"Added to Later, due {calendarEvent.LocalStart:ddd MMM d}."
            : "It couldn't be added.");
    }
}
