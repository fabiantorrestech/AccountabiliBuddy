using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// What the event menu does beyond a classification (ADR 64), and the same things asked from a phone. Everything that changes what an event counts as goes through
/// <see cref="EffectiveDayPlanProvider"/>, which refuses it during a session the user started and holds a looser change back.
/// </summary>
public sealed class CalendarEventActions
{
    private readonly EffectiveDayPlanProvider _provider;
    private readonly CalendarService _calendars;
    private readonly LaterStore _later;

    public CalendarEventActions(EffectiveDayPlanProvider provider, CalendarService calendars, LaterStore later)
    {
        _provider = provider;
        _calendars = calendars;
        _later = later;
    }

    /// <summary>A keyword rule can be made for this event: its calendar's rule doesn't already give every event one category.</summary>
    public bool CanMakeKeywordRule(CalendarEvent calendarEvent)
    {
        return EventCategoryResolver.IsUsable(calendarEvent) && _provider.Store.Rules.Active.RuleFor(calendarEvent.CalendarId)?.Mode != CalendarRuleMode.EveryEvent;
    }

    /// <summary>Adds the keyword to the rules, on top of a change already waiting; it waits the looser-change delay like any rule change.</summary>
    /// <returns>False, and nothing is saved, while a session runs or on a calendar that takes no keywords.</returns>
    public bool TrySaveKeywordRule(CalendarEvent calendarEvent, string keyword, Guid categoryId)
    {
        CalendarRules rules = _provider.Store.Rules;
        return !string.IsNullOrWhiteSpace(keyword)
            && EventKeywordRules.WithKeyword(rules.Pending ?? rules.Active, calendarEvent.CalendarId, keyword, categoryId) is CalendarRuleSet changed
            && _provider.TrySaveRules(changed);
    }

    /// <summary>A Later item titled after the event, due that day. It never touches today's three tasks.</summary>
    /// <returns>False when the list couldn't take it.</returns>
    public async Task<bool> AddTaskAsync(CalendarEvent calendarEvent)
    {
        return await _later.AddAsync(calendarEvent.Title, null, null, null, null, null, DateOnly.FromDateTime(calendarEvent.LocalStart)) is not null;
    }

    /// <summary>A phone's request for one of this PC's events: the same changes as the menu's.</summary>
    /// <returns>Whether it was taken, and what to tell the user; <paramref name="invalid"/> when it can't be understood or the event is gone, not just refused.</returns>
    public (bool Ok, string Message) Apply(SyncEventClassifyRequest request, out bool invalid)
    {
        invalid = false;
        if (Find(request) is not CalendarEvent calendarEvent)
        {
            invalid = true;
            return (false, "The PC no longer has this event.");
        }

        ClassificationScope scope = request.Scope == SyncEventClassifyRequest.ScopeThis ? ClassificationScope.ThisEvent : ClassificationScope.WholeSeries;
        bool accepted;
        switch (request.Scope, request.Choice)
        {
            case (SyncEventClassifyRequest.ScopeKeyword, SyncEventClassifyRequest.ChoiceCategory) when request.CategoryId is Guid category && request.Keyword is string keyword:
                accepted = TrySaveKeywordRule(calendarEvent, keyword, category);
                break;
            case (SyncEventClassifyRequest.ScopeThis or SyncEventClassifyRequest.ScopeSeries, SyncEventClassifyRequest.ChoiceCategory) when request.CategoryId is Guid category:
                accepted = _provider.TrySetClassification(calendarEvent, scope, category);
                break;
            case (SyncEventClassifyRequest.ScopeThis or SyncEventClassifyRequest.ScopeSeries, SyncEventClassifyRequest.ChoiceDontClassify):
                accepted = _provider.TrySetClassification(calendarEvent, scope, null);
                break;
            case (SyncEventClassifyRequest.ScopeThis or SyncEventClassifyRequest.ScopeSeries, SyncEventClassifyRequest.ChoiceAutomatic):
                accepted = _provider.TryRemoveClassification(calendarEvent, scope);
                break;
            default:
                invalid = true;
                return (false, "The PC couldn't read this request.");
        }

        return accepted ? (true, "Saved on the PC.") : (false, "Locked during the focus session on the PC.");
    }

    private CalendarEvent? Find(SyncEventClassifyRequest request)
    {
        string title = request.Title.Trim();
        return _calendars.EventsOn(DateOnly.FromDateTime(request.Start))
            .Select(entry => entry.Event)
            .FirstOrDefault(candidate => candidate.LocalStart == request.Start && candidate.LocalEnd == request.End
                && string.Equals(candidate.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
    }
}
