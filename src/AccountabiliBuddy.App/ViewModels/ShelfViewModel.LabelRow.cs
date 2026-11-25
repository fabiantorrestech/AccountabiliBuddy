using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Tasks redesign 4b (SPEC "Page layout" row 3): the label row ("All" · the labels in category order · +N) and its +N menu. Several labels
// can be picked (the view's working copy, Labels / NoLabel: any of them matches); "All" clears them. The row shows the whole chips that fit
// (the view measures: ChipsInRow); +N counts the rest plus No label. The menu lists every label by category; dragging a label within its
// category, or a category by its heading, is a stamped position edit that syncs (TaskLabelEdits.Reorder / TaskLabelGroupEdits.Reorder),
// and the row follows at once.
public sealed partial class ShelfViewModel
{
    /// <summary>The +N menu's last group: labels with no category, or whose category is missing or deleted.</summary>
    public const string OtherLabelsName = "Other";

    // "Other labels" has no icon of its own in the spec: the app's Label icon, as the label picker's rail (ADR 32).
    private const MaterialIconKind OtherLabelsIcon = MaterialIconKind.CircleSlice8;

    private readonly Dictionary<Guid, LabelFilterChipViewModel> _chipsById = [];
    private readonly Dictionary<Guid, LabelFilterGroupViewModel> _groupsById = [];
    private readonly LabelFilterGroupViewModel _otherLabels = new(null) { Name = OtherLabelsName, Icon = OtherLabelsIcon };

    /// <summary>Every label this device shows, in the row's order (category by category, then "Other labels"); the row shows the first <see cref="ChipsInRow"/>.</summary>
    public ObservableCollection<LabelFilterChipViewModel> RowChips { get; } = [];

    /// <summary>The +N menu's categories (then "Other labels"), each with its labels.</summary>
    public ObservableCollection<LabelFilterGroupViewModel> MenuGroups { get; } = [];

    /// <summary>How many whole chips fit in the row (set by the row as it measures); the rest are behind +N.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MoreText))]
    public partial int ChipsInRow { get; set; }

    /// <summary>"+5": the labels not in the row, plus No label.</summary>
    public string MoreText => "+" + (RowChips.Count - Math.Min(ChipsInRow, RowChips.Count) + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>No label is picked: "All" shows as picked.</summary>
    [ObservableProperty]
    public partial bool IsAllPicked { get; set; } = true;

    [ObservableProperty]
    public partial bool IsNoLabelPicked { get; set; }

    /// <summary>The view filters by label now (its working copy: a label shown here, or No label), whatever the row last showed.</summary>
    public bool FiltersByLabel => _views is not null && (ShownLabelIds(_views.Working.Labels).Count > 0 || _views.Working.NoLabel);

    /// <summary>A pick the row can't show (a label behind +N, or No label): +N stays lit.</summary>
    [ObservableProperty]
    public partial bool HasPickBehindMore { get; set; }

    /// <summary>The +N menu is open (the whole row is lit meanwhile).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLabelRowLit))]
    public partial bool IsLabelsMenuOpen { get; set; }

    /// <summary>"Fade controls you're not using" is off: the row is always fully lit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLabelRowLit))]
    public partial bool IsLabelRowAlwaysLit { get; set; }

    /// <summary>The whole row at full strength (its text in Ink, its dots in full colour), whatever the pointer does.</summary>
    public bool IsLabelRowLit => IsLabelsMenuOpen || IsLabelRowAlwaysLit;

    /// <summary>The +N menu's "New label": the page's naming row (the notice slot) asks for its name. Set by the page.</summary>
    public Action? NewLabelRequested { get; set; }

    private EventHandler? _fadeChanged;

    /// <summary>The row follows "Fade controls you're not using" (device-local).</summary>
    public void FollowFade(TaskLayoutStore layout)
    {
        IsLabelRowAlwaysLit = !layout.Current.FadeIdleControls;
        _layoutStore ??= layout;
        _fadeChanged = (_, _) => IsLabelRowAlwaysLit = !layout.Current.FadeIdleControls;
        layout.Changed += _fadeChanged;
    }

    partial void OnChipsInRowChanged(int value) => MarkRow();

    /// <summary>"All": every picked label (and No label) comes off.</summary>
    [RelayCommand]
    private void PickAllLabels()
    {
        _views?.Modify(config => config with { Labels = WithHiddenLabelIds(config.Labels, []), NoLabel = false });
    }

    /// <summary>The +N menu's "No label": added to the filter, or taken off.</summary>
    [RelayCommand]
    private void ToggleNoLabel()
    {
        _views?.Modify(config => config with { NoLabel = !config.NoLabel });
    }

    [RelayCommand]
    private void ToggleLabelsMenu() => IsLabelsMenuOpen = !IsLabelsMenuOpen;

    /// <summary>"New label": the menu closes and the naming row takes the keyboard.</summary>
    [RelayCommand]
    private void NewLabel()
    {
        IsLabelsMenuOpen = false;
        NewLabelRequested?.Invoke();
    }

    /// <summary>"Rename, recolour, delete": Settings › Tasks › Labels.</summary>
    [RelayCommand]
    private void ManageLabels()
    {
        IsLabelsMenuOpen = false;
        OpenLabels?.Invoke();
    }

    // A label chip or menu row: the label joins the filter, or leaves it (the others stay picked).
    private void ToggleLabel(Guid id)
    {
        _views?.Modify(config => config with { Labels = Toggled(config.Labels, id) });
    }

    /// <summary>The label moved to <paramref name="index"/> among its category's labels (drag or Alt+↑/↓); a synced position edit.</summary>
    public Task MoveLabelAsync(Guid labelId, int index)
    {
        LabelFilterGroupViewModel? group = MenuGroups.FirstOrDefault(candidate => candidate.Items.Any(item => item.Id == labelId));
        if (group is null || _store is null)
        {
            return Task.CompletedTask;
        }

        int from = IndexOf(group.Items, labelId);
        int to = Math.Clamp(index, 0, group.Items.Count - 1);
        if (from == to)
        {
            return Task.CompletedTask;
        }

        // The row and the menu follow at once; the store's change (and its sync) comes after.
        group.Items.Move(from, to);
        SyncCollection(RowChips, MenuGroups.SelectMany(item => item.Items).ToList());
        MarkRow();
        List<Guid> order = group.Items.Select(item => item.Id).ToList();
        return _store.ChangeAsync(null, (lists, _, now) => TaskLabelEdits.Reorder(lists, order, now));
    }

    /// <summary>The category moved to <paramref name="index"/> among the categories (drag or Alt+↑/↓), with all its labels; a synced position edit.</summary>
    public Task MoveGroupAsync(Guid groupId, int index)
    {
        List<LabelFilterGroupViewModel> movable = MenuGroups.Where(group => group.CanMove).ToList();
        int from = movable.FindIndex(group => group.GroupId == groupId);
        if (from < 0 || _store is null)
        {
            return Task.CompletedTask;
        }

        int to = Math.Clamp(index, 0, movable.Count - 1);
        if (from == to)
        {
            return Task.CompletedTask;
        }

        MenuGroups.Move(from, to);
        SyncCollection(RowChips, MenuGroups.SelectMany(item => item.Items).ToList());
        MarkRow();

        // Categories with no label this device shows aren't in the menu: they keep their places among the others.
        List<Guid> shown = MenuGroups.Where(group => group.CanMove).Select(group => group.GroupId!.Value).ToList();
        List<Guid> all = TaskLabelGroupBook.Live(_store.Lists.LabelGroups).Select(group => group.Id).ToList();
        List<Guid> order = InSlotsOf(all, shown);
        return _store.ChangeAsync(null, (lists, _, now) => TaskLabelGroupEdits.Reorder(lists, order, now));
    }

    /// <summary>Alt+↑/↓ on a label row: one place up or down within its category.</summary>
    public Task MoveLabelByAsync(Guid labelId, int step)
    {
        LabelFilterGroupViewModel? group = MenuGroups.FirstOrDefault(candidate => candidate.Items.Any(item => item.Id == labelId));
        return group is null ? Task.CompletedTask : MoveLabelAsync(labelId, IndexOf(group.Items, labelId) + step);
    }

    /// <summary>Alt+↑/↓ on a category heading: one place up or down among the categories.</summary>
    public Task MoveGroupByAsync(Guid groupId, int step)
    {
        int at = MenuGroups.Where(group => group.CanMove).ToList().FindIndex(group => group.GroupId == groupId);
        return at < 0 ? Task.CompletedTask : MoveGroupAsync(groupId, at + step);
    }

    // The row and the menu from the task list as it is now: the chip and group objects are kept (updated in place), so a pick fades in and
    // a reorder moves what is already drawn.
    private void SyncLabelRow()
    {
        if (_views is null || _store is not { IsActive: true } store)
        {
            RowChips.Clear();
            MenuGroups.Clear();
            return;
        }

        TaskViewConfig working = _views.Working;
        HashSet<Guid> picked = ShownLabelIds(working.Labels).ToHashSet();
        IReadOnlyList<TaskLabelGroup> groups = TaskLabelGroupBook.Live(store.Lists.LabelGroups);
        IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(groups);
        Dictionary<Guid, int> counts = store.Lists.Tasks
            .Where(item => item.Task.IsLive && item.Task.Bin != TaskBin.Done && item.Task.LabelId is not null)
            .GroupBy(item => item.Task.LabelId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var byGroup = new Dictionary<Guid, List<LabelFilterChipViewModel>>();
        var others = new List<LabelFilterChipViewModel>();
        var live = new HashSet<Guid>();
        foreach (TaskLabel label in LiveLabels())
        {
            TaskLabelGroup? group = TaskLabelGroupBook.GroupOf(label, liveGroups);
            LabelFilterChipViewModel chip = ChipFor(label.Id);
            Color color = Color.TryParse(TaskLabelColors.Opaque(label.Color), out Color parsed) ? parsed : Colors.Gray;
            chip.Update(label.Name, TaskLabelShortNames.Of(label.Name, group?.Name), color, counts.GetValueOrDefault(label.Id));
            chip.IsSelected = picked.Contains(label.Id);
            live.Add(label.Id);
            if (group is null)
            {
                others.Add(chip);
            }
            else
            {
                if (!byGroup.TryGetValue(group.Id, out List<LabelFilterChipViewModel>? list))
                {
                    byGroup[group.Id] = list = [];
                }

                list.Add(chip);
            }
        }

        foreach (Guid gone in _chipsById.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _chipsById.Remove(gone);
        }

        var menu = new List<LabelFilterGroupViewModel>();
        foreach (TaskLabelGroup group in groups)
        {
            // A category with no label shown here has nothing to pick: it isn't listed.
            if (!byGroup.TryGetValue(group.Id, out List<LabelFilterChipViewModel>? items))
            {
                continue;
            }

            if (!_groupsById.TryGetValue(group.Id, out LabelFilterGroupViewModel? item))
            {
                _groupsById[group.Id] = item = new LabelFilterGroupViewModel(group.Id);
            }

            item.Name = group.Name;
            item.Icon = LabelGroupIconKinds.Of(group.Icon);
            item.CountText = items.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
            SyncCollection(item.Items, items);
            menu.Add(item);
        }

        if (others.Count > 0)
        {
            _otherLabels.CountText = others.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
            SyncCollection(_otherLabels.Items, others);
            menu.Add(_otherLabels);
        }

        SyncCollection(MenuGroups, menu);
        SyncCollection(RowChips, menu.SelectMany(group => group.Items).ToList());

        IsNoLabelPicked = working.NoLabel;
        IsAllPicked = picked.Count == 0 && !working.NoLabel;
        MarkRow();
    }

    // Which chips are in the row (the rest carry "not in row"), +N's count, and whether +N holds a pick.
    private void MarkRow()
    {
        for (int index = 0; index < RowChips.Count; index++)
        {
            RowChips[index].InRow = index < ChipsInRow;
        }

        HasPickBehindMore = IsNoLabelPicked || RowChips.Any(chip => chip.IsSelected && !chip.InRow);
        OnPropertyChanged(nameof(MoreText));
    }

    private LabelFilterChipViewModel ChipFor(Guid id)
    {
        if (!_chipsById.TryGetValue(id, out LabelFilterChipViewModel? chip))
        {
            _chipsById[id] = chip = new LabelFilterChipViewModel(id, ToggleLabel);
        }

        return chip;
    }

    private static int IndexOf(IList<LabelFilterChipViewModel> items, Guid id)
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (items[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }

    // The ids of all in their order, with the shown ones put back into the shown ones' places in their new order.
    private static List<Guid> InSlotsOf(IReadOnlyList<Guid> all, IReadOnlyList<Guid> shownInOrder)
    {
        var shown = shownInOrder.ToHashSet();
        var next = new Queue<Guid>(shownInOrder);
        return all.Select(id => shown.Contains(id) ? next.Dequeue() : id).ToList();
    }

    // The collection made equal to wanted with as few moves as it takes (an unchanged order changes nothing on screen).
    private static void SyncCollection<T>(ObservableCollection<T> collection, IReadOnlyList<T> wanted) where T : class
    {
        for (int index = 0; index < wanted.Count; index++)
        {
            if (index < collection.Count && ReferenceEquals(collection[index], wanted[index]))
            {
                continue;
            }

            int at = -1;
            for (int search = index + 1; search < collection.Count; search++)
            {
                if (ReferenceEquals(collection[search], wanted[index]))
                {
                    at = search;
                    break;
                }
            }

            if (at >= 0)
            {
                collection.Move(at, index);
            }
            else
            {
                collection.Insert(index, wanted[index]);
            }
        }

        while (collection.Count > wanted.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }
}
