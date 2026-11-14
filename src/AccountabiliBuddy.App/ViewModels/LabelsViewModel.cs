using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Labels screen (Settings › Tasks › Labels; spec 03 §C, 05 §16, prototype's Labels group): "‹ Tasks", "Labels · 5" with +, one
/// row per label (<see cref="LabelRowViewModel"/>: colour, rename in place, "3 tasks", Rings on ▾, delete with confirm) and the new
/// label row (its colour dot pre-picks the first colour of the set no label has; Enter adds it). Every "Manage labels" entry opens
/// it (the Shelf chips' +, Settings › Tasks › Labels, the View menu's Labels ›). Edits go through <see cref="TaskLabelService"/> (stamped,
/// synced) and say what they did in the Tasks pages' toast. Not locked during a session: labels loosen nothing.
/// </summary>
public sealed partial class LabelsViewModel : ObservableObject
{
    private readonly TaskLabelService _labels;
    private readonly Func<IReadOnlyList<TaskRingDevice>> _devices;

    // The new label's colour was picked by hand (it no longer follows "the first colour no label has").
    private bool _newColorChosen;

    public LabelsViewModel(TaskLabelService labels, Func<IReadOnlyList<TaskRingDevice>> devices)
    {
        _labels = labels;
        _devices = devices;
        NewName = CountText = string.Empty;
        NewColor = labels.NextColor();
        NewColorBrush = TaskCategoryBrushes.Of(TaskLabelColors.ArgbHex(NewColor));
        NewColorEditor = new LabelColorEditorViewModel(NewColor, color =>
        {
            NewColor = color;
            _newColorChosen = true;
        });
        labels.Changed += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    /// <summary>The Tasks pages' toast (shown at the screen's bottom left too); null: nothing is said.</summary>
    public TaskToastViewModel? Toasts { get; init; }

    /// <summary>"‹ Tasks": back to Settings › Tasks.</summary>
    public Action? Close { get; init; }

    public ObservableCollection<LabelRowViewModel> Rows { get; } = new();

    /// <summary>"5 labels", "1 label", "No labels yet".</summary>
    [ObservableProperty]
    public partial string CountText { get; set; }

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The new label row's name box ("New label, then Enter").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewNameTaken))]
    public partial string NewName { get; set; }

    /// <summary>The name typed is another label's: the box shows red.</summary>
    public bool IsNewNameTaken => _labels.CheckName(null, NewName) == TaskLabelNameCheck.Taken;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewColorBrush))]
    public partial string NewColor { get; set; }

    public IBrush NewColorBrush { get; private set; }

    public LabelColorEditorViewModel NewColorEditor { get; }

    /// <summary>The new label's "Only on this device" (off: a new label is shared).</summary>
    [ObservableProperty]
    public partial bool NewOnlyHere { get; set; }

    /// <summary>"Only on this device" is offered once this device is in a sync group.</summary>
    public bool CanScope => _labels.CanScope;

    /// <summary>The tags this device shows, each with its scope switch (ADR 89).</summary>
    public ObservableCollection<TagRowViewModel> TagRows { get; } = new();

    public bool HasTags => TagRows.Count > 0;

    /// <summary>The + in the header: the new label row's name box takes the keyboard (the view does it).</summary>
    public event EventHandler? FocusNewRequested;

    /// <summary>The label list as the task list now has it: rows kept by id and updated in place, new ones added, gone ones removed.</summary>
    public void Refresh()
    {
        IReadOnlyList<TaskLabel> labels = _labels.Labels;
        IReadOnlyList<TaskRingDevice> devices = _devices();
        var wanted = labels.Select(label => label.Id).ToList();
        for (int index = Rows.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Rows[index].Id))
            {
                Rows.RemoveAt(index);
            }
        }

        for (int index = 0; index < labels.Count; index++)
        {
            TaskLabel label = labels[index];
            int uses = _labels.UseCount(label.Id);
            LabelRowViewModel? row = Rows.FirstOrDefault(candidate => candidate.Id == label.Id);
            if (row is null)
            {
                Rows.Insert(index, new LabelRowViewModel(label, uses, devices, _labels, Say));
                continue;
            }

            row.Update(label, uses, devices);
            int at = Rows.IndexOf(row);
            if (at != index)
            {
                Rows.Move(at, index);
            }
        }

        CountText = Rows.Count switch
        {
            0 => "No labels yet",
            1 => "1 label",
            _ => $"{Rows.Count} labels",
        };
        if (!_newColorChosen)
        {
            NewColor = _labels.NextColor();
        }

        RefreshTags();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsNewNameTaken));
        OnPropertyChanged(nameof(CanScope));
    }

    // The tags in use, kept by name and updated in place.
    private void RefreshTags()
    {
        IReadOnlyList<string> tags = _labels.Tags;
        for (int index = TagRows.Count - 1; index >= 0; index--)
        {
            if (!tags.Any(tag => TaskLabels.Same(tag, TagRows[index].Name)))
            {
                TagRows.RemoveAt(index);
            }
        }

        for (int index = 0; index < tags.Count; index++)
        {
            TagRowViewModel? row = TagRows.FirstOrDefault(candidate => TaskLabels.Same(candidate.Name, tags[index]));
            if (row is null)
            {
                TagRows.Insert(Math.Min(index, TagRows.Count), new TagRowViewModel(tags[index], _labels, Say));
                continue;
            }

            row.Update();
            int at = TagRows.IndexOf(row);
            if (at != index)
            {
                TagRows.Move(at, Math.Min(index, TagRows.Count - 1));
            }
        }

        OnPropertyChanged(nameof(HasTags));
    }

    partial void OnNewColorChanged(string value)
    {
        NewColorBrush = TaskCategoryBrushes.Of(TaskLabelColors.ArgbHex(value));
    }

    /// <summary>Enter in the new label row: added in the colour shown; a blank or taken name adds nothing and says why.</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        string name = NewName.Trim().TrimStart('@').Trim();
        TaskLabelNameCheck check = _labels.CheckName(null, name);
        if (check != TaskLabelNameCheck.Fine)
        {
            Say(check == TaskLabelNameCheck.Blank ? "Give the label a name first" : $"There’s already a label called “{name}”");
            return;
        }

        string color = NewColor;
        NewName = string.Empty;
        _newColorChosen = false;
        bool onlyHere = NewOnlyHere && CanScope;
        NewOnlyHere = false;
        (Guid? id, string? joinedWith) = await _labels.AddScopedAsync(name, color, onlyHere);
        if (id is not null)
        {
            // A device-only label other devices made under this name: this device joined it (ADR 89), never made it shared.
            Say(joinedWith is not null
                ? $"“{name}” is also on {joinedWith}. It is on this device now too; Make shared puts it on all your devices."
                : onlyHere ? $"Added label “{name}”, only on this device" : $"Added label “{name}”");
        }
    }

    [RelayCommand]
    private void FocusNew()
    {
        FocusNewRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Back()
    {
        Close?.Invoke();
    }

    private void Say(string text)
    {
        Toasts?.Say(text);
    }
}
