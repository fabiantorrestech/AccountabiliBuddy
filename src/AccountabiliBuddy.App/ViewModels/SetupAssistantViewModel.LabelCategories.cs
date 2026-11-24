using System.Collections.ObjectModel;
using System.ComponentModel;
using AccountabiliBuddy.App.Services.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's "Label categories" page (ADR 93; a new feature for upgraded installs only): the categories suggested from the name
/// prefixes several labels share, all ticked, and "Shorten the names" (off). Create makes the ticked ones and moves their labels in (and renames
/// them when asked, skipping a name that would clash); with nothing ticked the button says Not now. Either way the page counts as answered on this
/// PC, so it isn't offered again.
/// </summary>
public sealed partial class SetupAssistantViewModel
{
    /// <summary>The suggestions and their making; null without the task list (the page is then never offered).</summary>
    public LabelGroupSetupService? LabelCategories { get; init; }

    public bool IsLabelCategories => Step == AssistantStep.LabelCategories;

    /// <summary>One row for each suggested category.</summary>
    public ObservableCollection<LabelCategoryRowViewModel> CategoryRows { get; } = [];

    public bool HasCategoryRows => CategoryRows.Count > 0;

    /// <summary>"Shorten the names": each label in a ticked category is renamed to the part after the prefix ("Work - AI" to "AI"). Off by default.</summary>
    [ObservableProperty]
    public partial bool ShortenLabelNames { get; set; }

    private bool AnyCategoryTicked => CategoryRows.Any(row => row.IsTicked);

    private void OnLabelCategoriesStepShown(AssistantStep step)
    {
        if (step != AssistantStep.LabelCategories || LabelCategories is not { } setup)
        {
            return;
        }

        foreach (LabelCategoryRowViewModel row in CategoryRows)
        {
            row.PropertyChanged -= OnCategoryRowChanged;
        }

        CategoryRows.Clear();
        foreach (Core.Tasks.LabelGroupSuggestion suggestion in setup.Suggestions())
        {
            var row = new LabelCategoryRowViewModel(suggestion);
            row.PropertyChanged += OnCategoryRowChanged;
            CategoryRows.Add(row);
        }

        OnPropertyChanged(nameof(HasCategoryRows));
        OnPropertyChanged(nameof(NextText));
    }

    // A tick turns the button between Create and Not now.
    private void OnCategoryRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LabelCategoryRowViewModel.IsTicked))
        {
            OnPropertyChanged(nameof(NextText));
        }
    }

    // Next on the page: the ticked categories are made (Create), or nothing (Not now); either way the page is answered on this PC.
    private async Task FinishLabelCategoriesStepAsync()
    {
        if (LabelCategories is { } setup && AnyCategoryTicked)
        {
            List<Core.Tasks.LabelGroupSuggestion> chosen = CategoryRows.Where(row => row.IsTicked).Select(row => row.Suggestion).ToList();
            int renamed = await setup.CreateAsync(chosen, ShortenLabelNames);
            Summary = chosen.Count == 1 ? "1 category made." : $"{chosen.Count} categories made.";
            if (ShortenLabelNames)
            {
                Summary += renamed == 1 ? " 1 label renamed." : $" {renamed} labels renamed.";
            }
        }

        if (SkipFeature is not null)
        {
            await SkipFeature(Services.Setup.SetupFeatures.LabelCategoriesId);
        }
    }
}
