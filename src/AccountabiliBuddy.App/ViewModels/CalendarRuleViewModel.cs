using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The rule choice under one calendar row (ADR 58): Off (the default), Keywords or Every event → category. It is a draft:
/// nothing takes effect until <see cref="CalendarRulesViewModel"/> saves the whole set, and then only after the wait.
/// </summary>
public sealed partial class CalendarRuleViewModel : ObservableObject
{
    private readonly CalendarRulesViewModel _owner;

    public CalendarRuleViewModel(CalendarRulesViewModel owner, string calendarId, string calendarName)
    {
        _owner = owner;
        CalendarId = calendarId;
        CalendarName = calendarName;
    }

    public CalendarRulesViewModel Owner => _owner;

    public string CalendarId { get; }

    public string CalendarName { get; }

    public IReadOnlyList<RuleModeChoice> Modes => RuleModeChoice.All;

    public IReadOnlyList<RuleCategoryChoice> Categories => _owner.Categories;

    /// <summary>This calendar's own keyword list, used when <see cref="UsesOwnList"/> is on.</summary>
    public ObservableCollection<KeywordRowViewModel> OwnKeywords { get; } = new();

    private RuleModeChoice _selectedMode = RuleModeChoice.Off;

    /// <summary>
    /// The rule choice. A null written back by the box (its list was rebuilt under it) is ignored, so the mode is never lost.
    /// </summary>
    public RuleModeChoice SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is not null && SetProperty(ref _selectedMode, value))
            {
                OnPropertyChanged(nameof(ShowsRule));
                OnPropertyChanged(nameof(IsKeywords));
                OnPropertyChanged(nameof(IsEveryEvent));
                _owner.NotifyEdited();
            }
        }
    }

    /// <summary>The keyword list is this calendar's own instead of the shared one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownKeywords), nameof(ListHint))]
    public partial bool UsesOwnList { get; set; }

    /// <summary>"Waiting until 4:05 PM. In force now: Off." when a saved change to this calendar's rule has yet to take over; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWaiting))]
    public partial string? WaitingText { get; set; }

    public bool HasWaiting => WaitingText is not null;

    public Guid? CategoryId { get; private set; }

    /// <summary>The category every event gets, for Every event.</summary>
    public RuleCategoryChoice? SelectedCategory
    {
        get => _owner.Categories.FirstOrDefault(choice => choice.Id == CategoryId);
        set
        {
            if (value is not null && value.Id != CategoryId)
            {
                CategoryId = value.Id;
                OnPropertyChanged();
                _owner.NotifyEdited();
            }
        }
    }

    /// <summary>The rows under the choice show only when the rule isn't Off.</summary>
    public bool ShowsRule => SelectedMode.Mode != CalendarRuleMode.Off;

    public bool IsKeywords => SelectedMode.Mode == CalendarRuleMode.Keywords;

    public bool IsEveryEvent => SelectedMode.Mode == CalendarRuleMode.EveryEvent;

    /// <summary>The list the keyword rows edit: this calendar's own, or the one every calendar without its own shares.</summary>
    public ObservableCollection<KeywordRowViewModel> ShownKeywords => UsesOwnList ? OwnKeywords : _owner.SharedKeywords;

    public string ListHint => UsesOwnList
        ? "Only this calendar uses this list."
        : "The shared list: every calendar that doesn't have its own list uses it, so a change here reaches them all.";

    /// <summary>Sets the draft from the saved rule (null = Off).</summary>
    public void Load(CalendarRule? rule)
    {
        SelectedMode = RuleModeChoice.Of(rule?.Mode ?? CalendarRuleMode.Off);
        CategoryId = rule?.CategoryId;
        OnPropertyChanged(nameof(SelectedCategory));
        UsesOwnList = rule?.OwnKeywords is not null;
        OwnKeywords.Clear();
        foreach (KeywordCategory keyword in rule?.OwnKeywords ?? [])
        {
            OwnKeywords.Add(_owner.NewKeywordRow(keyword.Keyword, keyword.CategoryId, OwnKeywords));
        }
    }

    /// <summary>The rule this draft stands for; null when it is Off.</summary>
    public CalendarRule? ToRule()
    {
        switch (SelectedMode.Mode)
        {
            case CalendarRuleMode.EveryEvent:
                return new CalendarRule(CalendarId, CalendarRuleMode.EveryEvent, CategoryId);
            case CalendarRuleMode.Keywords:
                return new CalendarRule(CalendarId, CalendarRuleMode.Keywords, null, UsesOwnList ? OwnKeywords.Select(row => row.ToKeyword()).OfType<KeywordCategory>().ToList() : null);
            default:
                return null;
        }
    }

    /// <summary>What is wrong with this draft, in words; null when it can be saved.</summary>
    public string? Problem()
    {
        if (IsEveryEvent && SelectedCategory is null)
        {
            return $"Pick a category for {CalendarName}.";
        }

        if (IsKeywords && ShownKeywords.Any(row => row.NeedsCategory))
        {
            return $"Pick a category for each keyword of {CalendarName}.";
        }

        return null;
    }

    public void CategoriesChanged()
    {
        OnPropertyChanged(nameof(SelectedCategory));
        foreach (KeywordRowViewModel row in OwnKeywords)
        {
            row.CategoriesChanged();
        }
    }

    partial void OnUsesOwnListChanged(bool value) => _owner.NotifyEdited();

    [RelayCommand]
    private void AddKeyword()
    {
        ObservableCollection<KeywordRowViewModel> list = ShownKeywords;
        list.Add(_owner.NewKeywordRow(string.Empty, _owner.Categories.FirstOrDefault()?.Id, list));
        _owner.NotifyEdited();
    }
}
