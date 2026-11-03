using System.Collections.ObjectModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The small picker on one calendar event of the Plan timeline (ADR 58): the day plan categories except Sleep and Pre-Work,
/// "Don't classify" and, when a choice exists, "Back to automatic". For a repeating event the choice covers the whole series
/// (the default) or only this one. Every change goes through <see cref="EffectiveDayPlanProvider"/>, which refuses it while a
/// session runs and holds a looser one back for the looser-change delay; the picker only says which of the two happened.
/// </summary>
public sealed partial class CalendarEventPickerViewModel : ObservableObject
{
    private readonly EffectiveDayPlanProvider _provider;
    private readonly ConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly CalendarEvent _event;

    /// <param name="calendarName">The event's calendar, for the line under the title.</param>
    /// <param name="close">Closes the picker.</param>
    /// <param name="openChangeADay">Opens the month view on the event's day, where clicking an event used to lead.</param>
    public CalendarEventPickerViewModel(
        EffectiveDayPlanProvider provider, ConfigurationService configuration, TimeProvider time, CalendarEvent calendarEvent,
        string calendarName, Action close, Action openChangeADay)
    {
        _provider = provider;
        _configuration = configuration;
        _time = time;
        _event = calendarEvent;
        CalendarName = calendarName;
        CloseAction = close;
        OpenChangeADayAction = openChangeADay;
        IsWholeSeries = true;
        Refresh();
    }

    public string Title => _event.Title;

    public string CalendarName { get; }

    /// <summary>"Tue Nov 3 · 2:00-3:00 PM".</summary>
    public string WhenText
    {
        get
        {
            string day = _event.LocalStart.ToString("ddd MMM d", CultureInfo.InvariantCulture);
            return $"{day} · {ClockRangeText.Of(_event.LocalStart, _event.LocalEnd)}";
        }
    }

    /// <summary>The event repeats: the choice can cover the whole series or just this one.</summary>
    public bool IsSeries => _event.SeriesId is not null;

    /// <summary>The event has a length and isn't over a week long; any other can't carry a category.</summary>
    public bool CanClassify => EventCategoryResolver.IsUsable(_event);

    public bool CannotClassify => !CanClassify;

    public Action CloseAction { get; }

    public Action OpenChangeADayAction { get; }

    [RelayCommand]
    private void Close() => CloseAction();

    [RelayCommand]
    private void OpenChangeADay() => OpenChangeADayAction();

    /// <summary>The choice covers every occurrence (the default); only meaningful for a repeating event.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsThisOne))]
    public partial bool IsWholeSeries { get; set; }

    public bool IsThisOne
    {
        get => !IsWholeSeries;
        set => IsWholeSeries = !value;
    }

    public ObservableCollection<EventCategoryChoiceViewModel> Choices { get; } = new();

    /// <summary>"Now: Comms." or "Now: the day plan decides."</summary>
    [ObservableProperty]
    public partial string NowText { get; set; } = string.Empty;

    /// <summary>What the last click did: applied at once, waits until a time, or refused.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    public bool HasMessage => Message is not null;

    /// <summary>A focus session the user started runs (or is still being put back): nothing can be changed.</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    public string LockText => "Locked while a focus session you started runs.";

    partial void OnIsWholeSeriesChanged(bool value)
    {
        Message = null;
        Refresh();
    }

    private ClassificationScope Scope => IsSeries && IsWholeSeries ? ClassificationScope.WholeSeries : ClassificationScope.ThisEvent;

    /// <summary>Reads the stored and waiting choices again (the page calls it when they change).</summary>
    public void Refresh()
    {
        IsLocked = _provider.SessionRuns;
        DayPlan plan = _configuration.Current.DayPlan;
        CalendarOverlayStore store = _provider.Store;
        EventClassification target = CalendarOverlayStore.TargetFor(_event, Scope, null);
        EventClassification? stored = store.Classifications.FirstOrDefault(choice => SameTarget(choice, target));
        PendingClassification? waiting = store.PendingClassifications.FirstOrDefault(pending => SameTarget(pending.Change, target));

        DayCategory? now = EventCategoryResolver.CategoryOf(_event, plan, store.Classifications, store.Rules.Active);
        NowText = now is null ? "Now: the day plan decides." : $"Now: {now.Name}.";

        Choices.Clear();
        foreach (DayCategory category in plan.Categories.Where(category => category.Behavior is not (CategoryBehavior.Sleep or CategoryBehavior.PreWork)))
        {
            Choices.Add(new EventCategoryChoiceViewModel(
                category.Name,
                EventChoiceKind.Category,
                category.Id,
                new SolidColorBrush(Color.Parse(category.Color)),
                stored?.CategoryId == category.Id,
                WaitFor(waiting, removes: false, category.Id),
                Choose));
        }

        Choices.Add(new EventCategoryChoiceViewModel(
            "Don't classify", EventChoiceKind.DontClassify, null, null, stored is { CategoryId: null }, WaitFor(waiting, removes: false, null), Choose));
        if (stored is not null)
        {
            Choices.Add(new EventCategoryChoiceViewModel(
                "Back to automatic", EventChoiceKind.Automatic, null, null, false, WaitFor(waiting, removes: true, null), Choose));
        }
    }

    // The waiting change is shown on the row it would make current: a category, Don't classify, or Back to automatic.
    private string? WaitFor(PendingClassification? waiting, bool removes, Guid? categoryId)
    {
        return waiting is not null && waiting.Removes == removes && (removes || waiting.Change.CategoryId == categoryId)
            ? CalendarWaitText.TakesEffect(waiting.TakesOverAt, _time)
            : null;
    }

    private void Choose(EventCategoryChoiceViewModel choice)
    {
        if (!CanClassify)
        {
            return;
        }

        bool accepted = choice.Kind switch
        {
            EventChoiceKind.Automatic => _provider.TryRemoveClassification(_event, Scope),
            _ => _provider.TrySetClassification(_event, Scope, choice.CategoryId),
        };

        Refresh();
        if (!accepted)
        {
            Message = "Not changed: a focus session you started is running.";
            return;
        }

        EventClassification target = CalendarOverlayStore.TargetFor(_event, Scope, null);
        PendingClassification? waiting = _provider.Store.PendingClassifications.FirstOrDefault(pending => SameTarget(pending.Change, target));
        Message = waiting is null
            ? "Saved. It applies at once."
            : $"{CalendarWaitText.TakesEffect(waiting.TakesOverAt, _time)}. Until then the earlier choice stays.";
    }

    private static bool SameTarget(EventClassification first, EventClassification second)
    {
        return first.CalendarId == second.CalendarId && first.Scope == second.Scope && first.Key == second.Key;
    }
}
