using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One category on the Labels screen (Tasks redesign 10.15-10.21, ADR 93): its heading (drag grip, icon picker, name renamed in place,
/// "3 labels", delete with confirm) and its labels (<see cref="LabelRowViewModel"/>) indented under it; or the last section, "Other labels"
/// (<see cref="Id"/> null): the labels with no category, or whose category is missing or deleted. A category is always shared (no "Only on
/// this device"). Deleting one leaves a tombstone and never edits its labels: they show under "Other labels". Kept by id and updated in place,
/// so a name being typed or an open flyout isn't lost.
/// </summary>
public sealed partial class LabelCategorySectionViewModel : ObservableObject
{
    /// <summary>The last section's heading.</summary>
    public const string OtherName = "Other labels";

    private readonly TaskLabelService _labels;
    private readonly Action<string> _say;
    private string _savedName;

    /// <param name="group">The category; null for "Other labels".</param>
    public LabelCategorySectionViewModel(TaskLabelGroup? group, TaskLabelService labels, Action<string> say)
    {
        _labels = labels;
        _say = say;
        Id = group?.Id;
        _savedName = group?.Name ?? OtherName;
        Name = _savedName;
        IconKey = group?.Icon ?? TaskLabelGroupIcons.Default;
        CountText = DeleteText = string.Empty;
        IconChoices = LabelGroupIconChoiceViewModel.All(IconKey, PickIcon);
        if (group is not null)
        {
            Update(group);
        }
    }

    /// <summary>The category's id; null for "Other labels".</summary>
    public Guid? Id { get; }

    /// <summary>A real category (grip, icon picker, rename, delete); false for "Other labels".</summary>
    public bool IsCategory => Id is not null;

    public bool IsOther => Id is null;

    /// <summary>The heading shows: always for a category; for "Other labels" only once a category exists (before that it is the whole list).</summary>
    [ObservableProperty]
    public partial bool ShowsHeading { get; set; } = true;

    public ObservableCollection<LabelRowViewModel> Rows { get; } = new();

    public bool IsEmpty => Rows.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNameBad))]
    public partial string Name { get; set; }

    /// <summary>The name being typed is blank or another category's: the box shows red and the name isn't kept.</summary>
    public bool IsNameBad => IsCategory && _labels.CheckGroupName(Id, Name) != TaskLabelNameCheck.Fine;

    /// <summary>The stored icon key (an unknown key shows the default icon).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    public partial string IconKey { get; set; }

    /// <summary>The heading's icon: the category's, or the Label icon for "Other labels" (as the label picker's rail).</summary>
    public MaterialIconKind Icon => IsCategory ? LabelGroupIconKinds.Of(IconKey) : MaterialIconKind.CircleSlice8;

    /// <summary>The icon picker's grid (24 Material icons).</summary>
    public IReadOnlyList<LabelGroupIconChoiceViewModel> IconChoices { get; }

    /// <summary>"No labels", "1 label", "3 labels".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    /// <summary>"Delete category · its 3 labels move to Other labels".</summary>
    [ObservableProperty]
    public partial string DeleteText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotConfirmingDelete))]
    public partial bool IsConfirmingDelete { get; set; }

    public bool IsNotConfirmingDelete => !IsConfirmingDelete;

    /// <summary>The category as the task list now has it (here or from another device); a name being typed stays as typed.</summary>
    public void Update(TaskLabelGroup group)
    {
        if (Name.Trim() == _savedName)
        {
            Name = group.Name;
        }

        _savedName = group.Name;
        IconKey = group.Icon;
        foreach (LabelGroupIconChoiceViewModel choice in IconChoices)
        {
            choice.IsPicked = string.Equals(choice.Key, TaskLabelGroupIcons.Chosen(group.Icon), StringComparison.Ordinal);
        }

        OnPropertyChanged(nameof(IsNameBad));
    }

    /// <summary>The labels it holds now, in order: rows kept by instance, moved into place, others taken out (they sit elsewhere now).</summary>
    public void SetRows(IReadOnlyList<LabelRowViewModel> rows)
    {
        for (int index = Rows.Count - 1; index >= 0; index--)
        {
            if (!rows.Contains(Rows[index]))
            {
                Rows.RemoveAt(index);
            }
        }

        for (int index = 0; index < rows.Count; index++)
        {
            int at = Rows.IndexOf(rows[index]);
            if (at < 0)
            {
                Rows.Insert(index, rows[index]);
            }
            else if (at != index)
            {
                Rows.Move(at, index);
            }
        }

        CountText = Rows.Count switch
        {
            0 => "No labels",
            1 => "1 label",
            _ => $"{Rows.Count} labels",
        };
        DeleteText = Rows.Count switch
        {
            0 => "Delete category",
            1 => "Delete · its label moves to Other labels",
            _ => $"Delete · its {Rows.Count} labels move to Other labels",
        };
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Enter or clicking away: a good new name is kept; a blank or taken one is put back, with a toast saying why.</summary>
    [RelayCommand]
    private async Task SaveNameAsync()
    {
        if (Id is not Guid id)
        {
            return;
        }

        string trimmed = Name.Trim();
        if (trimmed == _savedName)
        {
            return;
        }

        string old = _savedName;
        TaskLabelNameCheck check = _labels.CheckGroupName(id, trimmed);
        if (check != TaskLabelNameCheck.Fine)
        {
            Name = old;
            _say(check == TaskLabelNameCheck.Blank ? "A category needs a name" : $"There’s already a category called “{trimmed}”");
            return;
        }

        _savedName = trimmed;
        Name = trimmed;
        await _labels.RenameGroupAsync(id, trimmed);
        _say($"Renamed category “{old}” to “{trimmed}”");
    }

    /// <summary>Esc in the name box: the name as it was.</summary>
    [RelayCommand]
    private void CancelName()
    {
        Name = _savedName;
    }

    [RelayCommand]
    private void AskDelete()
    {
        IsConfirmingDelete = true;
    }

    [RelayCommand]
    private void KeepIt()
    {
        IsConfirmingDelete = false;
    }

    /// <summary>The category goes on every device; its labels stay as they are and show under "Other labels" (ADR 93).</summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        IsConfirmingDelete = false;
        if (Id is not Guid id)
        {
            return;
        }

        string name = _savedName;
        int count = Rows.Count;
        await _labels.DeleteGroupAsync(id);
        _say(count == 0 ? $"Deleted category “{name}”" : $"Deleted category “{name}” · its labels are under Other labels now");
    }

    private void PickIcon(string key)
    {
        if (Id is not Guid id || string.Equals(key, IconKey, StringComparison.Ordinal))
        {
            return;
        }

        IconKey = key;
        foreach (LabelGroupIconChoiceViewModel choice in IconChoices)
        {
            choice.IsPicked = string.Equals(choice.Key, key, StringComparison.Ordinal);
        }

        _ = _labels.SetGroupIconAsync(id, key);
    }
}
