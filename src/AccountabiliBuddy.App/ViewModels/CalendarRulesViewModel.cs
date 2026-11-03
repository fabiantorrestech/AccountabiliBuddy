using System.Collections.ObjectModel;
using System.Text.Json;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The per-calendar rules of Settings › Connections (ADR 58) as one draft: the choice under each calendar, and the shared
/// keyword list. Saving always goes through <see cref="EffectiveDayPlanProvider.TrySaveRules"/>: the new set waits for the
/// looser-change delay, the rules in force stay until then, and a running session refuses it. The draft starts from the set
/// that waits, if one does, else from the rules in force, and survives the calendar list being rebuilt.
/// </summary>
public sealed partial class CalendarRulesViewModel : ObservableObject
{
    private readonly EffectiveDayPlanProvider _provider;
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly Dictionary<string, CalendarRuleViewModel> _rules = new();
    private bool _loading;

    public CalendarRulesViewModel(EffectiveDayPlanProvider provider, ConfigurationService configuration, SessionController sessions)
    {
        _provider = provider;
        _configuration = configuration;
        _sessions = sessions;
        RefreshCategories(notify: false);
        LoadAll();
        RefreshState();
        // The store raises its event on any thread, inside a lock: only post to the UI thread from here.
        _provider.Store.Changed += (_, _) => Dispatcher.UIThread.Post(OnStoreChanged);
        _configuration.Changed += (_, _) => Dispatcher.UIThread.Post(OnPlanChanged);
        _sessions.StateChanged += (_, _) => Dispatcher.UIThread.Post(RefreshState);
    }

    /// <summary>The categories a rule can give: every one of the plan except Sleep and Pre-Work.</summary>
    public ObservableCollection<RuleCategoryChoice> Categories { get; } = new();

    /// <summary>The keyword list every calendar without its own list shares.</summary>
    public ObservableCollection<KeywordRowViewModel> SharedKeywords { get; } = new();

    /// <summary>A focus session the user started runs (or is still being put back): the controls are disabled.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyPropertyChangedFor(nameof(IsUnlocked))]
    public partial bool IsLocked { get; set; }

    public bool IsUnlocked => !IsLocked;

    public string LockText => "Calendar rules can't be changed while a focus session you started runs.";

    /// <summary>The draft differs from the saved set.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(DiscardCommand))]
    public partial bool IsEdited { get; set; }

    /// <summary>"New rules take effect at 4:05 PM. Until then the rules in force stay."; null when none waits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPending))]
    public partial string? PendingText { get; set; }

    public bool HasPending => PendingText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    public bool HasMessage => Message is not null;

    /// <summary>The draft of one calendar's rule; the same object for the same calendar until it is gone.</summary>
    public CalendarRuleViewModel For(string calendarId, string calendarName)
    {
        if (!_rules.TryGetValue(calendarId, out CalendarRuleViewModel? rule))
        {
            rule = new CalendarRuleViewModel(this, calendarId, calendarName);
            _rules[calendarId] = rule;
            Load(rule);
            RefreshWaiting();
        }

        return rule;
    }

    /// <summary>Drops the drafts of calendars that are no longer listed, so a later save can't bring their rules back.</summary>
    public void KeepOnly(IReadOnlyCollection<string> calendarIds)
    {
        foreach (string gone in _rules.Keys.Where(id => !calendarIds.Contains(id)).ToList())
        {
            _rules.Remove(gone);
        }
    }

    internal KeywordRowViewModel NewKeywordRow(string keyword, Guid? categoryId, ObservableCollection<KeywordRowViewModel> list)
    {
        return new KeywordRowViewModel(this, keyword, categoryId, removed =>
        {
            list.Remove(removed);
            NotifyEdited();
        });
    }

    /// <summary>A control of the draft changed.</summary>
    internal void NotifyEdited()
    {
        if (_loading)
        {
            return;
        }

        Message = null;
        IsEdited = Json(DraftSet()) != Json(Baseline());
    }

    private bool CanSave => IsEdited && !IsLocked;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_provider.SessionRuns)
        {
            IsLocked = true;
            Message = "Not saved: a focus session you started is running.";
            return;
        }

        string? problem = _rules.Values.Select(rule => rule.Problem()).FirstOrDefault(text => text is not null)
            ?? (SharedKeywords.Any(row => row.NeedsCategory) ? "Pick a category for each shared keyword." : null);
        if (problem is not null)
        {
            Message = problem;
            return;
        }

        if (!_provider.TrySaveRules(DraftSet()))
        {
            IsLocked = true;
            Message = "Not saved: a focus session you started is running.";
            return;
        }

        LoadAll();
        RefreshState();
        Message = $"{PendingText}";
    }

    [RelayCommand(CanExecute = nameof(IsEdited))]
    private void Discard()
    {
        LoadAll();
        RefreshState();
        Message = null;
    }

    private void OnStoreChanged()
    {
        // A waiting set took over or was replaced: the draft follows unless it is being edited.
        if (!IsEdited)
        {
            LoadAll();
        }

        RefreshState();
    }

    private void OnPlanChanged()
    {
        RefreshCategories(notify: true);
        RefreshState();
    }

    private void RefreshState()
    {
        IsLocked = _provider.SessionRuns;
        CalendarRules rules = _provider.Store.Rules;
        PendingText = rules.Pending is not null && rules.PendingTakesOverAt is DateTimeOffset at
            ? $"{CalendarWaitText.TakesEffect(at, _sessions.TimeProvider)}. Until then the rules in force stay."
            : null;
        RefreshWaiting();
        IsEdited = Json(DraftSet()) != Json(Baseline());
    }

    // The set the draft starts from: the waiting one if there is one (the latest intent), else the rules in force.
    private CalendarRuleSet Baseline()
    {
        CalendarRules rules = _provider.Store.Rules;
        return Normalized(rules.Pending ?? rules.Active);
    }

    private void LoadAll()
    {
        _loading = true;
        try
        {
            CalendarRuleSet baseline = Baseline();
            SharedKeywords.Clear();
            foreach (KeywordCategory keyword in baseline.GlobalKeywords)
            {
                SharedKeywords.Add(NewKeywordRow(keyword.Keyword, keyword.CategoryId, SharedKeywords));
            }

            foreach (CalendarRuleViewModel rule in _rules.Values)
            {
                Load(rule);
            }
        }
        finally
        {
            _loading = false;
        }

        IsEdited = false;
    }

    private void Load(CalendarRuleViewModel rule)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            rule.Load(Baseline().RuleFor(rule.CalendarId));
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // The set the draft stands for: a rule for each calendar shown that isn't Off, the rules of calendars not shown as they were.
    private CalendarRuleSet DraftSet()
    {
        CalendarRuleSet baseline = Baseline();
        List<CalendarRule> rules = baseline.Rules.Where(rule => !_rules.ContainsKey(rule.CalendarId)).ToList();
        rules.AddRange(_rules.Values.Select(draft => draft.ToRule()).OfType<CalendarRule>());
        List<KeywordCategory> shared = SharedKeywords.Select(row => row.ToKeyword()).OfType<KeywordCategory>().ToList();
        return Normalized(new CalendarRuleSet(rules, shared));
    }

    // Off rules dropped, in a fixed order, so two sets that mean the same compare the same.
    private static CalendarRuleSet Normalized(CalendarRuleSet set)
    {
        return new CalendarRuleSet(
            set.Rules.Where(rule => rule.Mode != CalendarRuleMode.Off).OrderBy(rule => rule.CalendarId, StringComparer.Ordinal).ToList(),
            set.GlobalKeywords);
    }

    private static string Json(CalendarRuleSet set) => JsonSerializer.Serialize(set);

    // Marks each calendar whose waiting rule differs from the one in force.
    private void RefreshWaiting()
    {
        CalendarRules rules = _provider.Store.Rules;
        foreach (CalendarRuleViewModel draft in _rules.Values)
        {
            if (rules.Pending is null || rules.PendingTakesOverAt is not DateTimeOffset at || Meaning(rules.Pending, draft.CalendarId) == Meaning(rules.Active, draft.CalendarId))
            {
                draft.WaitingText = null;
                continue;
            }

            draft.WaitingText = $"{CalendarWaitText.WaitingUntil(at, _sessions.TimeProvider)}. In force now: {Describe(rules.Active, draft.CalendarId)}.";
        }
    }

    // What a calendar's rule amounts to in a set: the mode, the category and the keywords it really uses.
    private static string Meaning(CalendarRuleSet set, string calendarId)
    {
        CalendarRule? rule = set.RuleFor(calendarId);
        if (rule is null || rule.Mode == CalendarRuleMode.Off)
        {
            return "off";
        }

        return rule.Mode == CalendarRuleMode.EveryEvent
            ? $"every|{rule.CategoryId}"
            : "keywords|" + JsonSerializer.Serialize(set.KeywordsFor(rule));
    }

    private string Describe(CalendarRuleSet set, string calendarId)
    {
        CalendarRule? rule = set.RuleFor(calendarId);
        if (rule is null || rule.Mode == CalendarRuleMode.Off)
        {
            return "Off";
        }

        if (rule.Mode == CalendarRuleMode.EveryEvent)
        {
            return $"Every event → {CategoryName(rule.CategoryId)}";
        }

        int count = set.KeywordsFor(rule).Count;
        return count == 1 ? "Keywords (1 keyword)" : $"Keywords ({count} keywords)";
    }

    private string CategoryName(Guid? id)
    {
        return id is Guid category && _configuration.Current.DayPlan.Category(category) is DayCategory found ? found.Name : "a category that is gone";
    }

    private void RefreshCategories(bool notify)
    {
        List<RuleCategoryChoice> fresh = _configuration.Current.DayPlan.Categories
            .Where(category => category.Behavior is not (CategoryBehavior.Sleep or CategoryBehavior.PreWork))
            .Select(category => new RuleCategoryChoice(category.Id, category.Name))
            .ToList();
        if (Categories.SequenceEqual(fresh))
        {
            return;
        }

        Categories.Clear();
        foreach (RuleCategoryChoice choice in fresh)
        {
            Categories.Add(choice);
        }

        if (notify)
        {
            // The boxes lose their selection when the list is rebuilt; the drafts keep the ids and show them again.
            foreach (CalendarRuleViewModel rule in _rules.Values)
            {
                rule.CategoriesChanged();
            }

            foreach (KeywordRowViewModel row in SharedKeywords)
            {
                row.CategoriesChanged();
            }
        }
    }
}
