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
/// Tasks redesign 10.15-10.21 (ADR 93): the rows sit under their categories (<see cref="Sections"/>: each with its icon and name, "Other
/// labels" last); "+ Category" adds one; a label is dragged to another category or to a place in its own, and a category by its grip (every
/// move a stamped edit that syncs; Alt+Up/Down on a grip does the same from the keyboard).
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

    /// <summary>The categories in their order, each with its labels, then "Other labels" (its heading shows once a category exists).</summary>
    public ObservableCollection<LabelCategorySectionViewModel> Sections { get; } = new();

    /// <summary>At least one category exists.</summary>
    public bool HasCategories => Sections.Any(section => section.IsCategory);

    /// <summary>"+ Category" was pressed: the new category row shows (icon picker, name box).</summary>
    [ObservableProperty]
    public partial bool IsAddingCategory { get; set; }

    /// <summary>The new category's name box ("Category name, then Enter").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewCategoryNameTaken))]
    public partial string NewCategoryName { get; set; } = string.Empty;

    /// <summary>The name typed is another category's: the box shows red.</summary>
    public bool IsNewCategoryNameTaken => _labels.CheckGroupName(null, NewCategoryName) == TaskLabelNameCheck.Taken;

    /// <summary>The new category's icon key (the default folder until one is picked).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewCategoryIcon))]
    public partial string NewCategoryIconKey { get; set; } = TaskLabelGroupIcons.Default;

    public Material.Icons.MaterialIconKind NewCategoryIcon => LabelGroupIconKinds.Of(NewCategoryIconKey);

    /// <summary>The new category row's icon picker.</summary>
    public IReadOnlyList<LabelGroupIconChoiceViewModel> NewCategoryIconChoices { get; private set; } = [];

    /// <summary>The new label row's category choice: every category with its icon, then "Other labels" (no category).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<LabelCategoryChoice> NewLabelCategories { get; set; } = [];

    /// <summary>
    /// The category a new label goes in: the one the user was last working in (picked here, a category just added, a label just moved
    /// into one) while it exists, else "Other labels".
    /// </summary>
    [ObservableProperty]
    public partial LabelCategoryChoice? NewLabelCategory { get; set; }

    // The category last worked in (null: none yet, or "Other labels").
    private Guid? _lastCategoryId;

    /// <summary>"+ Category": the new category row's name box takes the keyboard (the view does it).</summary>
    public event EventHandler? FocusNewCategoryRequested;

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

        RefreshSections(labels);
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

    // The categories (kept by id, updated in place) with their labels in the list's order, then "Other labels".
    private void RefreshSections(IReadOnlyList<TaskLabel> labels)
    {
        IReadOnlyList<TaskLabelGroup> groups = _labels.Groups;
        IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(groups);
        var rowsByGroup = new Dictionary<Guid, List<LabelRowViewModel>>();
        var others = new List<LabelRowViewModel>();
        foreach (TaskLabel label in labels)
        {
            LabelRowViewModel? row = Rows.FirstOrDefault(candidate => candidate.Id == label.Id);
            if (row is null)
            {
                continue;
            }

            if (TaskLabelGroupBook.GroupOf(label, liveGroups) is { } group)
            {
                if (!rowsByGroup.TryGetValue(group.Id, out List<LabelRowViewModel>? list))
                {
                    rowsByGroup[group.Id] = list = [];
                }

                list.Add(row);
            }
            else
            {
                others.Add(row);
            }
        }

        var wanted = new List<LabelCategorySectionViewModel>();
        foreach (TaskLabelGroup group in groups)
        {
            LabelCategorySectionViewModel section = Sections.FirstOrDefault(candidate => candidate.Id == group.Id)
                ?? new LabelCategorySectionViewModel(group, _labels, Say);
            section.Update(group);
            section.SetRows(rowsByGroup.TryGetValue(group.Id, out List<LabelRowViewModel>? rows) ? rows : []);
            wanted.Add(section);
        }

        // "Other labels": with no category it is the whole list (its heading hides); with categories it stays, a drop target even when empty.
        LabelCategorySectionViewModel other = Sections.FirstOrDefault(candidate => candidate.IsOther) ?? new LabelCategorySectionViewModel(null, _labels, Say);
        other.SetRows(others);
        wanted.Add(other);

        for (int index = Sections.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(Sections[index]))
            {
                Sections.RemoveAt(index);
            }
        }

        for (int index = 0; index < wanted.Count; index++)
        {
            int at = Sections.IndexOf(wanted[index]);
            if (at < 0)
            {
                Sections.Insert(index, wanted[index]);
            }
            else if (at != index)
            {
                Sections.Move(at, index);
            }
        }

        bool hasCategories = HasCategories;
        other.ShowsHeading = hasCategories;
        RefreshNewLabelCategories(groups);
        OnPropertyChanged(nameof(HasCategories));
    }

    // The new label row's choices, and its pick: the category last worked in while it is live, else "Other labels".
    private void RefreshNewLabelCategories(IReadOnlyList<TaskLabelGroup> groups)
    {
        List<LabelCategoryChoice> choices = groups
            .Select(group => new LabelCategoryChoice(group.Id, group.Name, LabelGroupIconKinds.Of(group.Icon)))
            .Append(new LabelCategoryChoice(null, LabelCategorySectionViewModel.OtherName, Material.Icons.MaterialIconKind.CircleSlice8))
            .ToList();
        Guid? wanted = _lastCategoryId is Guid last && choices.Any(choice => choice.Id == last) ? last : null;
        NewLabelCategories = choices;
        NewLabelCategory = choices.First(choice => choice.Id == wanted);
    }

    partial void OnNewLabelCategoryChanged(LabelCategoryChoice? value)
    {
        if (value is not null)
        {
            _lastCategoryId = value.Id;
        }
    }

    // ---- Moves (drag, or Alt+Up/Down on a grip) ----

    /// <summary>
    /// A label dropped into <paramref name="target"/> at <paramref name="index"/> (counted among that section's other labels): into that
    /// category if it isn't there, and in that place. Nothing happens when it lands where it was.
    /// </summary>
    public async Task MoveLabelAsync(LabelRowViewModel row, LabelCategorySectionViewModel target, int index)
    {
        LabelCategorySectionViewModel? from = Sections.FirstOrDefault(section => section.Rows.Contains(row));
        List<Guid> order = target.Rows.Where(other => !ReferenceEquals(other, row)).Select(other => other.Id).ToList();
        order.Insert(Math.Clamp(index, 0, order.Count), row.Id);
        if (ReferenceEquals(from, target) && order.SequenceEqual(target.Rows.Select(other => other.Id)))
        {
            return;
        }

        if (target.Id is Guid workedIn)
        {
            _lastCategoryId = workedIn;
        }

        await _labels.MoveLabelAsync(row.Id, target.Id, order);
        if (!ReferenceEquals(from, target))
        {
            Say($"Moved “{row.Name}” to {target.Name}");
        }
    }

    /// <summary>Alt+Up/Down on a label's grip: one place up or down; past the first or last of its category, into the next one's end or start.</summary>
    public Task MoveLabelByAsync(LabelRowViewModel row, int step)
    {
        int sectionAt = Sections.ToList().FindIndex(section => section.Rows.Contains(row));
        if (sectionAt < 0)
        {
            return Task.CompletedTask;
        }

        LabelCategorySectionViewModel section = Sections[sectionAt];
        int at = section.Rows.IndexOf(row) + step;
        if (at >= 0 && at < section.Rows.Count)
        {
            return MoveLabelAsync(row, section, at);
        }

        int nextAt = sectionAt + Math.Sign(step);
        if (nextAt < 0 || nextAt >= Sections.Count)
        {
            return Task.CompletedTask;
        }

        LabelCategorySectionViewModel next = Sections[nextAt];
        return MoveLabelAsync(row, next, step < 0 ? next.Rows.Count : 0);
    }

    /// <summary>A category dropped at <paramref name="index"/> among the other categories ("Other labels" stays last).</summary>
    public Task MoveCategoryAsync(LabelCategorySectionViewModel section, int index)
    {
        if (section.Id is not Guid id)
        {
            return Task.CompletedTask;
        }

        List<Guid> order = Sections.Where(other => other.IsCategory && !ReferenceEquals(other, section)).Select(other => other.Id!.Value).ToList();
        order.Insert(Math.Clamp(index, 0, order.Count), id);
        bool same = order.SequenceEqual(Sections.Where(other => other.IsCategory).Select(other => other.Id!.Value));
        return same ? Task.CompletedTask : _labels.ReorderGroupsAsync(order);
    }

    /// <summary>Alt+Up/Down on a category's grip: one place up or down among the categories.</summary>
    public Task MoveCategoryByAsync(LabelCategorySectionViewModel section, int step)
    {
        int at = Sections.Where(other => other.IsCategory).ToList().IndexOf(section);
        return at < 0 || at + step < 0 ? Task.CompletedTask : MoveCategoryAsync(section, at + step);
    }

    // ---- + Category ----

    [RelayCommand]
    private void StartAddCategory()
    {
        NewCategoryIconKey = TaskLabelGroupIcons.Default;
        NewCategoryIconChoices = LabelGroupIconChoiceViewModel.All(NewCategoryIconKey, PickNewCategoryIcon);
        OnPropertyChanged(nameof(NewCategoryIconChoices));
        IsAddingCategory = true;
        FocusNewCategoryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelAddCategory()
    {
        IsAddingCategory = false;
        NewCategoryName = string.Empty;
    }

    /// <summary>Enter in the new category row: added with the icon shown; a blank or taken name adds nothing and says why.</summary>
    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        string name = NewCategoryName.Trim();
        TaskLabelNameCheck check = _labels.CheckGroupName(null, name);
        if (check != TaskLabelNameCheck.Fine)
        {
            Say(check == TaskLabelNameCheck.Blank ? "Give the category a name first" : $"There’s already a category called “{name}”");
            return;
        }

        string icon = NewCategoryIconKey;
        NewCategoryName = string.Empty;
        IsAddingCategory = false;
        if (await _labels.AddGroupAsync(name, icon) is Guid added)
        {
            _lastCategoryId = added;
            Refresh();
            Say($"Added category “{name}”. Drag labels into it.");
        }
    }

    private void PickNewCategoryIcon(string key)
    {
        NewCategoryIconKey = key;
        foreach (LabelGroupIconChoiceViewModel choice in NewCategoryIconChoices)
        {
            choice.IsPicked = string.Equals(choice.Key, key, StringComparison.Ordinal);
        }
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
        // Q1: the label goes in the category picked in the row, as one stamped change with the add.
        LabelCategoryChoice? category = NewLabelCategory;
        (Guid? id, string? joinedWith) = await _labels.AddScopedAsync(name, color, onlyHere, category?.Id);
        if (id is not null)
        {
            string where = category?.Id is null ? string.Empty : $" to {category.Name}";
            // A device-only label other devices made under this name: this device joined it (ADR 89), never made it shared.
            Say(joinedWith is not null
                ? $"“{name}” is also on {joinedWith}. It is on this device now too; Make shared puts it on all your devices."
                : onlyHere ? $"Added label “{name}”{where}, only on this device" : $"Added label “{name}”{where}");
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
