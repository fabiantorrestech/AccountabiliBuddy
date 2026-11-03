using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One "keyword → category" line of a calendar rule's list (ADR 58), a draft until the rules are saved.</summary>
public sealed partial class KeywordRowViewModel : ObservableObject
{
    private readonly CalendarRulesViewModel _owner;
    private readonly Action<KeywordRowViewModel> _remove;

    public KeywordRowViewModel(CalendarRulesViewModel owner, string keyword, Guid? categoryId, Action<KeywordRowViewModel> remove)
    {
        _owner = owner;
        _remove = remove;
        Keyword = keyword;
        CategoryId = categoryId;
    }

    /// <summary>A word that, found in an event's title (any case), gives the event the category.</summary>
    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;

    public Guid? CategoryId { get; private set; }

    public IReadOnlyList<RuleCategoryChoice> Categories => _owner.Categories;

    /// <summary>
    /// The chosen category in the list. A null written back (the list was rebuilt under the choice) is ignored, so the
    /// choice survives a change of the plan's categories.
    /// </summary>
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

    /// <summary>Tells the row that the list of categories changed.</summary>
    public void CategoriesChanged() => OnPropertyChanged(nameof(SelectedCategory));

    /// <summary>The keyword as a rule keeps it; null for a blank line, which is left out.</summary>
    public KeywordCategory? ToKeyword()
    {
        string word = Keyword.Trim();
        return word.Length == 0 || CategoryId is not Guid id ? null : new KeywordCategory(word, id);
    }

    /// <summary>The line has a word but no category in the plan.</summary>
    public bool NeedsCategory => Keyword.Trim().Length > 0 && SelectedCategory is null;

    partial void OnKeywordChanged(string value) => _owner.NotifyEdited();

    [RelayCommand]
    private void Remove() => _remove(this);
}
