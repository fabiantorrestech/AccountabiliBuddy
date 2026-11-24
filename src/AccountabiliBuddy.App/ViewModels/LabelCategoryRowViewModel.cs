using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One suggested category on the setup assistant's "Label categories" page (ADR 93): its tick (on), its name, the labels that would go in, and
/// what "Shorten the names" would make of them.
/// </summary>
public sealed partial class LabelCategoryRowViewModel : ObservableObject
{
    public LabelCategoryRowViewModel(LabelGroupSuggestion suggestion)
    {
        Suggestion = suggestion;
        LabelsText = string.Join(", ", suggestion.Labels.Select(item => item.Name));
        ShortText = "Shortened: " + string.Join(", ", suggestion.Labels.Select(item => item.Skipped ? item.Name : item.ShortName));
        SkippedText = string.Join(" ", suggestion.Labels.Where(item => item.Skipped).Select(item => item.Name + ": " + item.SkipReason));
    }

    public LabelGroupSuggestion Suggestion { get; }

    public string Name => Suggestion.Name;

    /// <summary>A category that already exists takes the labels as it is.</summary>
    public string? ExistingText => Suggestion.ExistingGroupId is null ? null : "Already a category: the labels join it.";

    public bool HasExisting => Suggestion.ExistingGroupId is not null;

    public MaterialIconKind Icon => LabelGroupIconKinds.Of(null);

    /// <summary>The labels that would go in ("Work - AI, Work - Ops").</summary>
    public string LabelsText { get; }

    /// <summary>The names after "Shorten the names" ("Shortened: AI, Ops").</summary>
    public string ShortText { get; }

    /// <summary>The labels that keep their names, and why; empty when none.</summary>
    public string SkippedText { get; }

    public bool HasSkipped => SkippedText.Length > 0;

    [ObservableProperty]
    public partial bool IsTicked { get; set; } = true;
}
