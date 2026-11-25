using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One category of the view editor's label filter (Tasks redesign 10.35, ADR 93): its heading (icon and name) over its labels' ticks. A click
/// on a category's heading ticks all its labels, or takes them all off when they are all on; the view stores label ids only (no new view
/// field), so a label added to the category later isn't picked up by itself. "Other labels" (labels with no category, and "No label") has a
/// plain heading.
/// </summary>
public sealed partial class ViewEditorLabelGroup : ObservableObject
{
    /// <param name="isCategory">A real category (its heading picks its labels); false for "Other labels".</param>
    public ViewEditorLabelGroup(string name, MaterialIconKind icon, bool isCategory, IReadOnlyList<ViewEditorOption> options)
    {
        Name = name;
        Icon = icon;
        IsCategory = isCategory;
        Options = options;
        foreach (ViewEditorOption option in options)
        {
            option.PropertyChanged += OnOptionChanged;
        }
    }

    public string Name { get; }

    public MaterialIconKind Icon { get; }

    public bool IsCategory { get; }

    public bool IsOther => !IsCategory;

    public IReadOnlyList<ViewEditorOption> Options { get; }

    /// <summary>The heading's tick: all on (CheckboxMarked), some (MinusBoxOutline) or none (CheckboxBlankOutline), as the tag states.</summary>
    public MaterialIconKind StateIcon => Options.All(option => option.IsChecked) ? MaterialIconKind.CheckboxMarked
        : Options.Any(option => option.IsChecked) ? MaterialIconKind.MinusBoxOutline
        : MaterialIconKind.CheckboxBlankOutline;

    /// <summary>"Show tasks with any of Work's labels" / "Take Work's labels off".</summary>
    public string PickTip => Options.Count > 0 && Options.All(option => option.IsChecked)
        ? $"Take {Name}'s labels off"
        : $"Show tasks with any of {Name}'s labels";

    /// <summary>The heading: every label of the category on, or (when they all are) every one off.</summary>
    [RelayCommand]
    private void Pick()
    {
        bool allOn = Options.All(option => option.IsChecked);
        foreach (ViewEditorOption option in Options)
        {
            option.IsChecked = !allOn;
        }
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewEditorOption.IsChecked))
        {
            OnPropertyChanged(nameof(StateIcon));
            OnPropertyChanged(nameof(PickTip));
        }
    }
}
