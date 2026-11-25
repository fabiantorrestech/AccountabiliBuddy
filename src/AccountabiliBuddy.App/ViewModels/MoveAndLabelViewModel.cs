using System.Globalization;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Move and label (Tasks redesign 8c; SPEC "Move and label dialog"; board 21, 31; checklist 8c.01-8c.69): the tasks brought along on the
/// left, the choices for the ones being edited on the right. It opens editing every row not ticked; a click edits just one row, Ctrl+click
/// adds or takes one out, Shift+click a range, Ctrl+A all shown. The picks (a bin, a label or No label, tags on or off) show on the edited
/// rows as proposed changes and in the change line; Apply changes the edited tasks that would change at once, as one undo step with a
/// toast, ticks them (the tick is this dialog's progress, not the task's done state), and leaves nothing being edited and nothing picked.
/// Undoing that Apply takes its ticks back too. The dialog stays open until Done, × or Esc; closing clears the page's selection (the host).
/// </summary>
public sealed partial class MoveAndLabelViewModel : ObservableObject, IDisposable
{
    private const string ProgressTail = " done · each Apply saves right away (Undo in the corner)";
    private const double DimmedChoicesOpacity = 0.45;

    // A picked label's colour behind its pill: 17% (hex alpha 2B; checklist 8c.19, SPEC wins over the mockup's 30).
    private const byte PickedLabelAlpha = 0x2B;

    private static readonly TaskBin[] BinOrder = [TaskBin.Queued, TaskBin.Next, TaskBin.Sort, TaskBin.Later];

    private readonly IMoveAndLabelHost _host;
    private readonly List<MoveAndLabelRowViewModel> _rows;
    private readonly Dictionary<Guid, TaskItem> _tasks = [];
    private readonly HashSet<Guid> _editing = [];
    private readonly HashSet<Guid> _ticked = [];
    private readonly Dictionary<TaskUndoEntry, List<Guid>> _appliedTicks = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _tagOrder = [];
    private readonly Dictionary<string, MoveAndLabelTagPick> _tagPicks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _typedTags = [];
    private readonly Dictionary<Guid, (IBrush Dot, IBrush Fill)> _labelBrushes = [];
    private readonly MoveAndLabelNewTagSlot _newTagSlot;
    private Guid? _anchor;
    private TaskBin? _binPick;
    private bool _applying;

    internal MoveAndLabelViewModel(IMoveAndLabelHost host, IReadOnlyList<Guid> ids)
    {
        _host = host;
        _newTagSlot = new MoveAndLabelNewTagSlot(this);
        _rows = ids.Distinct().Select(id => new MoveAndLabelRowViewModel(id, Tick)).ToList();
        Bins = BinOrder.Select(bin => new MoveAndLabelBinViewModel(bin, BinName(bin), PickBin)).ToList();
        ShownRows = [];
        ShowOptions = [];
        TagChips = [];
        ChangeRows = [];
        NewTagText = string.Empty;
        ChangeText = string.Empty;
        ShowCountText = string.Empty;
        EditingText = string.Empty;
        BinNowText = string.Empty;
        LabelNowText = string.Empty;
        ProgressText = string.Empty;
        ApplyText = "Apply";
        LoadTasks();
        // It opens editing every row not ticked (plan item 8; nothing is ticked yet).
        _editing.UnionWith(_rows.Select(row => row.Id));
        Picker = NewPicker(LabelPick.Nothing, string.Empty);
        Recompute();
    }

    /// <summary>"5 tasks brought along" / "1 task brought along".</summary>
    public string BroughtAlongText => TaskToasts.Count(_rows.Count) + " brought along";

    public IReadOnlyList<MoveAndLabelBinViewModel> Bins { get; }

    // ---- The left side ----

    /// <summary>The rows the Show dropdown lets through, in the order they were brought along.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MoveAndLabelRowViewModel> ShownRows { get; private set; }

    [ObservableProperty]
    public partial MoveAndLabelShow Show { get; private set; }

    /// <summary>The Show button's words: "All", "Not done yet", "Done".</summary>
    public string ShowName => NameOf(Show);

    /// <summary>The Show button's count: "(5)".</summary>
    [ObservableProperty]
    public partial string ShowCountText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<MoveAndLabelShowOptionViewModel> ShowOptions { get; private set; }

    [ObservableProperty]
    public partial bool IsShowMenuOpen { get; set; }

    /// <summary>"Editing 2 of 5".</summary>
    [ObservableProperty]
    public partial string EditingText { get; private set; }

    // ---- The right side ----

    /// <summary>The choices dim to 45% while no row is being edited (8c.30).</summary>
    [ObservableProperty]
    public partial double ChoicesOpacity { get; private set; }

    /// <summary>The change line's words (8c.34-8c.37).</summary>
    [ObservableProperty]
    public partial string ChangeText { get; private set; }

    /// <summary>A pick changes at least one task being edited: the line is lit and opens the table (8c.32, 8c.33).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChangeTable))]
    public partial bool IsChangeLit { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsChangeTable))]
    public partial bool IsChangeTableOpen { get; set; }

    public bool ShowsChangeTable => IsChangeTableOpen && IsChangeLit;

    [ObservableProperty]
    public partial IReadOnlyList<MoveAndLabelChangeRow> ChangeRows { get; private set; }

    /// <summary>"Now: To sort" or "Now: Mixed" (8c.42).</summary>
    [ObservableProperty]
    public partial string BinNowText { get; private set; }

    /// <summary>"Now: Work - ePT", "Now: No label" or "Now: Mixed" (8c.47).</summary>
    [ObservableProperty]
    public partial string LabelNowText { get; private set; }

    /// <summary>The shared label picker (category rail + search), tagged for the tasks being edited; rebuilt when they change.</summary>
    [ObservableProperty]
    public partial LabelPickerViewModel Picker { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagItems))]
    public partial IReadOnlyList<MoveAndLabelTagViewModel> TagChips { get; private set; }

    /// <summary>The tag chips, then the "+ New tag · Enter" box (one wrapping row).</summary>
    public IReadOnlyList<object> TagItems => [.. TagChips, _newTagSlot];

    /// <summary>"+ New tag · Enter".</summary>
    [ObservableProperty]
    public partial string NewTagText { get; set; }

    // ---- The footer ----

    /// <summary>"1 of 5 done · each Apply saves right away (Undo in the corner)".</summary>
    [ObservableProperty]
    public partial string ProgressText { get; private set; }

    /// <summary>Something is picked: Keep as is can clear it (8c.60).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(KeepAsIsCommand))]
    public partial bool HasPicks { get; private set; }

    /// <summary>"Apply (2)", "Apply" for one row (the counts rule; N = the rows being edited, 8c.61).</summary>
    [ObservableProperty]
    public partial string ApplyText { get; private set; }

    /// <summary>Something would change (8c.62).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    public partial bool CanApply { get; private set; }

    /// <summary>The ids brought along that are still live, in their order.</summary>
    public IReadOnlyList<Guid> Ids => _rows.Select(row => row.Id).ToList();

    /// <summary>The rows being edited, in their order.</summary>
    public IReadOnlyList<Guid> EditingIds => _rows.Where(row => _editing.Contains(row.Id)).Select(row => row.Id).ToList();

    /// <summary>The ticked rows, in their order.</summary>
    public IReadOnlyList<Guid> TickedIds => _rows.Where(row => _ticked.Contains(row.Id)).Select(row => row.Id).ToList();

    // ---- The rows ----

    /// <summary>A click on a row (8c.23): edits just it; Ctrl+click adds or takes it out; Shift+click adds the shown rows from the last picked one.</summary>
    public void Click(MoveAndLabelRowViewModel row, TaskRowClick click)
    {
        switch (click)
        {
            case TaskRowClick.Toggle:
                if (!_editing.Remove(row.Id))
                {
                    _editing.Add(row.Id);
                }

                _anchor = row.Id;
                break;
            case TaskRowClick.AddRange or TaskRowClick.Range:
                List<Guid> shown = ShownRows.Select(item => item.Id).ToList();
                int from = _anchor is Guid anchor ? shown.IndexOf(anchor) : -1;
                int to = shown.IndexOf(row.Id);
                if (from < 0 || to < 0)
                {
                    _editing.Add(row.Id);
                    _anchor = row.Id;
                    break;
                }

                _editing.UnionWith(shown.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1));
                break;
            default:
                _editing.Clear();
                _editing.Add(row.Id);
                _anchor = row.Id;
                break;
        }

        EditingChanged();
    }

    /// <summary>Ctrl+A with the list focused (8c.24): every row shown is edited.</summary>
    [RelayCommand]
    private void EditAllShown()
    {
        _editing.Clear();
        _editing.UnionWith(ShownRows.Select(row => row.Id));
        EditingChanged();
    }

    // The round tick (8c.28): set or cleared by hand; ticking one of several rows being edited ticks them all.
    private void Tick(MoveAndLabelRowViewModel row)
    {
        bool on = !_ticked.Contains(row.Id);
        IEnumerable<Guid> targets = row.IsEditing && _editing.Count > 1 ? _editing.ToList() : [row.Id];
        foreach (Guid id in targets)
        {
            if (on)
            {
                _ticked.Add(id);
            }
            else
            {
                _ticked.Remove(id);
            }
        }

        Recompute();
    }

    private void EditingChanged()
    {
        Picker = NewPicker(Picker.Pick, Picker.SearchText);
        Recompute();
    }

    // ---- Show ----

    [RelayCommand]
    private void ToggleShowMenu() => IsShowMenuOpen = !IsShowMenuOpen;

    private void PickShow(MoveAndLabelShow show)
    {
        Show = show;
        IsShowMenuOpen = false;
        Recompute();
    }

    partial void OnShowChanged(MoveAndLabelShow value) => OnPropertyChanged(nameof(ShowName));

    // ---- The picks ----

    private void PickBin(TaskBin bin)
    {
        _binPick = _binPick == bin ? null : bin;
        Recompute();
    }

    private void ClickTag(string tag)
    {
        if (_tagPicks.Remove(tag))
        {
            _tagOrder.RemoveAll(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            List<TaskItem> edited = EditedTasks();
            bool all = edited.Count > 0 && edited.All(task => TaskLabels.Has(_host.ShownTags(task.Tags), tag));
            SetTagPick(tag, all ? MoveAndLabelTagPick.Remove : MoveAndLabelTagPick.Add);
        }

        Recompute();
    }

    /// <summary>"+ New tag · Enter" (8c.56): the typed tag (a leading "#" dropped) is picked to go on all of them. False when nothing usable was typed.</summary>
    public bool AddNewTag()
    {
        if (TaskLabels.Clean([NewTagText.Trim().TrimStart('#')]) is not [string tag])
        {
            return false;
        }

        if (!_typedTags.Contains(tag, StringComparer.OrdinalIgnoreCase) && !_host.KnownTags().Contains(tag, StringComparer.OrdinalIgnoreCase))
        {
            _typedTags.Add(tag);
        }

        SetTagPick(tag, MoveAndLabelTagPick.Add);
        NewTagText = string.Empty;
        Recompute();
        return true;
    }

    private void SetTagPick(string tag, MoveAndLabelTagPick pick)
    {
        if (!_tagPicks.ContainsKey(tag))
        {
            _tagOrder.Add(tag);
        }

        _tagPicks[tag] = pick;
    }

    /// <summary>Keep as is (8c.59): every pick is cleared; bin, label and tags stay as they are.</summary>
    [RelayCommand(CanExecute = nameof(HasPicks))]
    private void KeepAsIs()
    {
        ClearPicks();
        Recompute();
    }

    private void ClearPicks()
    {
        _binPick = null;
        _tagPicks.Clear();
        _tagOrder.Clear();
        Picker.Pick = LabelPick.Nothing;
    }

    [RelayCommand]
    private void ToggleChangeTable()
    {
        if (IsChangeLit)
        {
            IsChangeTableOpen = !IsChangeTableOpen;
        }
    }

    /// <summary>
    /// Apply (8c.64): the picks on the edited tasks that would change, at once, as one undo step with the toast "Updated 2 tasks → Later · …";
    /// those tasks tick; then nothing is being edited and nothing is picked.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_applying)
        {
            return;
        }

        List<TaskItem> changing = EditedTasks().Where(Changes).ToList();
        if (changing.Count == 0)
        {
            return;
        }

        _applying = true;
        try
        {
            List<Guid> ids = changing.Select(task => task.Id).ToList();
            LabelPick label = Picker.Pick;
            var edit = new TaskBatchEdit(
                _binPick, !label.IsNothing, label.IsNoLabel ? null : label.LabelId,
                _tagOrder.Where(tag => _tagPicks[tag] == MoveAndLabelTagPick.Add).ToList(),
                _tagOrder.Where(tag => _tagPicks[tag] == MoveAndLabelTagPick.Remove).ToList());
            TaskUndoEntry? entry = await _host.EditManyAsync(ids, edit, TaskToasts.UpdatedCount(ids.Count, ChangeWords()));
            if (entry is not null)
            {
                List<Guid> newlyTicked = ids.Where(id => _ticked.Add(id)).ToList();
                _appliedTicks[entry] = newlyTicked;
                _editing.Clear();
                ClearPicks();
                Picker = NewPicker(LabelPick.Nothing, Picker.SearchText);
            }

            LoadTasks();
            Recompute();
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Done (and ×, Esc): the dialog closes and the page's selection clears (8c.06).</summary>
    [RelayCommand]
    private void Done() => _host.CloseMoveAndLabel();

    /// <summary>An undo put back an action: when it was one of this dialog's Applies, the ticks it set come off too (8c.66).</summary>
    internal void OnUndone(TaskUndoEntry entry)
    {
        if (_appliedTicks.Remove(entry, out List<Guid>? ticked))
        {
            _ticked.ExceptWith(ticked);
        }

        Refresh();
    }

    /// <summary>The task list changed (an Apply, an undo, another device): every row reads its task again.</summary>
    public void Refresh()
    {
        LoadTasks();
        Recompute();
    }

    public void Dispose()
    {
        Picker.PickChanged -= OnPickerPickChanged;
    }

    // ---- Reading and showing ----

    private void LoadTasks()
    {
        _tasks.Clear();
        foreach (MoveAndLabelRowViewModel row in _rows)
        {
            if (_host.TaskOf(row.Id) is { } task)
            {
                _tasks[row.Id] = task;
            }
        }

        // A task that is gone (deleted on another device, finished) leaves the list.
        _rows.RemoveAll(row => !_tasks.ContainsKey(row.Id));
        _editing.IntersectWith(_tasks.Keys);
        _ticked.IntersectWith(_tasks.Keys);
    }

    private LabelPickerViewModel NewPicker(LabelPick pick, string search)
    {
        if (Picker is { } old)
        {
            old.PickChanged -= OnPickerPickChanged;
        }

        List<Guid?> current = EditedTasks().Select(task => _host.ShownLabelOf(task)?.Id).ToList();
        LabelPickerViewModel picker = _host.NewLabelPicker(current, pick);
        picker.SearchText = search;
        picker.PickChanged += OnPickerPickChanged;
        return picker;
    }

    private void OnPickerPickChanged(object? sender, EventArgs e) => Recompute();

    private List<TaskItem> EditedTasks()
    {
        return _rows.Where(row => _editing.Contains(row.Id) && _tasks.ContainsKey(row.Id)).Select(row => _tasks[row.Id]).ToList();
    }

    private void Recompute()
    {
        List<TaskItem> edited = EditedTasks();
        int restCount = _rows.Count(row => !_ticked.Contains(row.Id));
        int doneCount = _rows.Count - restCount;

        // Left: the rows, the Show dropdown, "Editing 2 of 5".
        foreach (MoveAndLabelRowViewModel row in _rows)
        {
            Fill(row, _tasks[row.Id], _editing.Contains(row.Id), _ticked.Contains(row.Id));
        }

        ShownRows = _rows.Where(row => Show switch
        {
            MoveAndLabelShow.NotDone => !_ticked.Contains(row.Id),
            MoveAndLabelShow.Done => _ticked.Contains(row.Id),
            _ => true,
        }).ToList();
        ShowCountText = "(" + ShownRows.Count.ToString(CultureInfo.CurrentCulture) + ")";
        ShowOptions =
        [
            new MoveAndLabelShowOptionViewModel(MoveAndLabelShow.All, NameOf(MoveAndLabelShow.All), _rows.Count, Show == MoveAndLabelShow.All, PickShow),
            new MoveAndLabelShowOptionViewModel(MoveAndLabelShow.NotDone, NameOf(MoveAndLabelShow.NotDone), restCount, Show == MoveAndLabelShow.NotDone, PickShow),
            new MoveAndLabelShowOptionViewModel(MoveAndLabelShow.Done, NameOf(MoveAndLabelShow.Done), doneCount, Show == MoveAndLabelShow.Done, PickShow),
        ];
        EditingText = $"Editing {edited.Count.ToString(CultureInfo.CurrentCulture)} of {_rows.Count.ToString(CultureInfo.CurrentCulture)}";

        // Right: the bins, the label, the tags.
        ChoicesOpacity = edited.Count == 0 ? DimmedChoicesOpacity : 1;
        foreach (MoveAndLabelBinViewModel bin in Bins)
        {
            int here = edited.Count(task => task.Bin == bin.Bin);
            bin.Tag = here == 0 ? string.Empty : here == edited.Count ? "Current" : here.ToString(CultureInfo.CurrentCulture) + " here";
            bin.IsPicked = _binPick == bin.Bin;
        }

        List<TaskBin> binsNow = edited.Select(task => task.Bin).Distinct().ToList();
        BinNowText = binsNow.Count switch
        {
            0 => string.Empty,
            1 => "Now: " + BinName(binsNow[0]),
            _ => "Now: Mixed",
        };
        List<Guid?> labelsNow = edited.Select(task => _host.ShownLabelOf(task)?.Id).Distinct().ToList();
        LabelNowText = labelsNow.Count switch
        {
            0 => string.Empty,
            1 => "Now: " + (labelsNow[0] is Guid id && _host.LabelById(id) is { } label ? label.FullName : "No label"),
            _ => "Now: Mixed",
        };
        FillTagChips(edited);

        // The change line and its table.
        List<TaskItem> changing = edited.Where(Changes).ToList();
        IsChangeLit = changing.Count > 0;
        ChangeText = edited.Count == 0
            ? restCount > 0 ? "Pick tasks on the left to edit them." : "All done. Press Done, or pick a ticked task to change it again."
            : changing.Count == 0
                ? "Pick a bin, a label or tags for the " + TaskToasts.Count(edited.Count) + " you are editing."
                : TaskToasts.Count(changing.Count) + " will change: " + ChangeWords();
        ChangeRows = changing.Select(ChangeRowOf).ToList();

        // The footer.
        ProgressText = doneCount.ToString(CultureInfo.CurrentCulture) + " of " + _rows.Count.ToString(CultureInfo.CurrentCulture) + ProgressTail;
        HasPicks = _binPick is not null || !Picker.Pick.IsNothing || _tagPicks.Count > 0;
        ApplyText = SelectionCountText.Action("Apply", edited.Count);
        CanApply = changing.Count > 0;
    }

    // One row's lines: the current values, and on a row being edited the proposed change (8c.16-8c.22).
    private void Fill(MoveAndLabelRowViewModel row, TaskItem task, bool editing, bool ticked)
    {
        row.Title = task.Title;
        row.IsEditing = editing;
        row.IsTicked = ticked;

        row.BinIcon = TaskBinIcons.Redesigned(task.Bin);
        row.BinName = BinName(task.Bin);
        TaskBin? binTo = editing ? BinTo(task) : null;
        row.HasBinChange = binTo is not null;
        row.BinToIcon = binTo is TaskBin to ? TaskBinIcons.Redesigned(to) : MaterialIconKind.Tray;
        row.BinToName = binTo is TaskBin target ? BinName(target) : string.Empty;

        MoveAndLabelLabel? label = _host.ShownLabelOf(task);
        row.IsUnlabelled = label is null;
        row.LabelName = label?.FullName ?? "No label";
        row.LabelDot = label is null ? Brushes.Transparent : BrushesOf(label).Dot;
        MoveAndLabelLabel? labelTo = null;
        bool labelChanges = editing && LabelChanges(task, out labelTo);
        labelTo = labelChanges ? labelTo : null;
        row.HasLabelChange = labelChanges;
        row.IsLabelToNone = labelChanges && labelTo is null;
        row.LabelToName = labelChanges ? labelTo?.FullName ?? "No label" : string.Empty;
        row.LabelToDot = labelTo is null ? Brushes.Transparent : BrushesOf(labelTo).Dot;
        row.LabelToFill = labelTo is null ? Brushes.Transparent : BrushesOf(labelTo).Fill;

        IReadOnlyList<string> now = _host.ShownTags(task.Tags);
        var tags = now.Select(tag => new MoveAndLabelRowTag("#" + tag, editing && PickOf(tag) == MoveAndLabelTagPick.Remove, false)).ToList();
        if (editing)
        {
            tags.AddRange(_tagOrder
                .Where(tag => _tagPicks[tag] == MoveAndLabelTagPick.Add && !TaskLabels.Has(now, tag))
                .Select(tag => new MoveAndLabelRowTag("+#" + tag, false, true)));
        }

        if (!row.Tags.SequenceEqual(tags))
        {
            row.Tags = tags;
        }
    }

    // The tag chips: every tag this device shows, then the ones typed here; counts over the tasks being edited (8c.51-8c.54).
    private void FillTagChips(List<TaskItem> edited)
    {
        List<string> names = _host.KnownTags().ToList();
        foreach (string tag in _typedTags.Concat(_tagOrder))
        {
            if (!names.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(tag);
            }
        }

        if (!TagChips.Select(chip => chip.Tag).SequenceEqual(names, StringComparer.OrdinalIgnoreCase))
        {
            TagChips = names.Select(tag => new MoveAndLabelTagViewModel(tag, ClickTag)).ToList();
        }

        foreach (MoveAndLabelTagViewModel chip in TagChips)
        {
            int having = edited.Count(task => TaskLabels.Has(_host.ShownTags(task.Tags), chip.Tag));
            chip.Of = edited.Count;
            chip.Having = having;
            chip.Pick = PickOf(chip.Tag);
            chip.Note = chip.Pick switch
            {
                MoveAndLabelTagPick.Add => "on for all",
                MoveAndLabelTagPick.Remove => "off all",
                _ => having > 0 && having < edited.Count
                    ? having.ToString(CultureInfo.CurrentCulture) + " of " + edited.Count.ToString(CultureInfo.CurrentCulture)
                    : string.Empty,
            };
        }
    }

    // ---- What the picks do to one task ----

    private TaskBin? BinTo(TaskItem task) => _binPick is TaskBin bin && task.Bin != bin ? bin : null;

    // The label pick changes this task: it has another label (as shown here) than the one picked, or one when No label is picked.
    private bool LabelChanges(TaskItem task, out MoveAndLabelLabel? target)
    {
        target = null;
        LabelPick pick = Picker.Pick;
        if (pick.IsNothing)
        {
            return false;
        }

        target = LabelTarget();
        Guid? now = _host.ShownLabelOf(task)?.Id;
        return pick.IsNoLabel ? now is not null : target is not null && now != target.Id;
    }

    private MoveAndLabelLabel? LabelTarget() => Picker.Pick.LabelId is Guid id ? _host.LabelById(id) : null;

    private MoveAndLabelTagPick? PickOf(string tag) => _tagPicks.TryGetValue(tag, out MoveAndLabelTagPick pick) ? pick : null;

    private IReadOnlyList<string> TagsAfter(TaskItem task)
    {
        var after = _host.ShownTags(task.Tags).Where(tag => PickOf(tag) != MoveAndLabelTagPick.Remove).ToList();
        foreach (string tag in _tagOrder.Where(tag => _tagPicks[tag] == MoveAndLabelTagPick.Add))
        {
            if (!TaskLabels.Has(after, tag))
            {
                after.Add(tag);
            }
        }

        return after;
    }

    private bool TagsChange(TaskItem task)
    {
        IReadOnlyList<string> now = _host.ShownTags(task.Tags);
        IReadOnlyList<string> after = TagsAfter(task);
        return now.Count != after.Count || after.Any(tag => !TaskLabels.Has(now, tag));
    }

    private bool Changes(TaskItem task) => BinTo(task) is not null || LabelChanges(task, out _) || TagsChange(task);

    // "→ Later · → Work - AI · +#quick −#waiting" (8c.34).
    private string ChangeWords()
    {
        var parts = new List<string>();
        if (_binPick is TaskBin bin)
        {
            parts.Add("→ " + BinName(bin));
        }

        if (!Picker.Pick.IsNothing)
        {
            parts.Add("→ " + (Picker.Pick.IsNoLabel ? "No label" : LabelTarget()?.FullName ?? "No label"));
        }

        if (_tagOrder.Count > 0)
        {
            parts.Add(string.Join(" ", _tagOrder.Select(tag => (_tagPicks[tag] == MoveAndLabelTagPick.Add ? "+#" : "−#") + tag)));
        }

        return string.Join(" · ", parts);
    }

    // A row of the change table (8c.40): "To sort → Later" bold when it changes, "To sort (stays)" dim when it doesn't.
    private MoveAndLabelChangeRow ChangeRowOf(TaskItem task)
    {
        TaskBin? binTo = BinTo(task);
        string binText = binTo is TaskBin to ? BinName(task.Bin) + " → " + BinName(to) : BinName(task.Bin) + " (stays)";

        MoveAndLabelLabel? labelNow = _host.ShownLabelOf(task);
        bool labelChanges = LabelChanges(task, out MoveAndLabelLabel? labelTo);
        string labelNowText = labelNow?.ShortName ?? "No label";
        string labelText = labelChanges ? labelNowText + " → " + (labelTo?.ShortName ?? "No label") : labelNowText + " (stays)";

        IReadOnlyList<string> now = _host.ShownTags(task.Tags);
        bool tagsChange = TagsChange(task);
        string tagsText = tagsChange
            ? TagList(now) + " → " + TagList(TagsAfter(task))
            : now.Count > 0 ? TagList(now) + " (stay)" : "no tags";
        return new MoveAndLabelChangeRow(task.Title, binText, binTo is not null, labelText, labelChanges, tagsText, tagsChange);
    }

    private static string TagList(IReadOnlyList<string> tags) => tags.Count == 0 ? "none" : string.Join(" ", tags.Select(tag => "#" + tag));

    private (IBrush Dot, IBrush Fill) BrushesOf(MoveAndLabelLabel label)
    {
        if (!_labelBrushes.TryGetValue(label.Id, out (IBrush Dot, IBrush Fill) brushes) || ((SolidColorBrush)brushes.Dot).Color != label.Color)
        {
            Color color = label.Color;
            brushes = (new SolidColorBrush(color), new SolidColorBrush(Color.FromArgb(PickedLabelAlpha, color.R, color.G, color.B)));
            _labelBrushes[label.Id] = brushes;
        }

        return brushes;
    }

    /// <summary>A bin's name in this dialog: Queued is "Today" (SPEC: Today = Queued).</summary>
    private static string BinName(TaskBin bin) => bin == TaskBin.Queued ? "Today" : TaskBins.Name(bin);

    private static string NameOf(MoveAndLabelShow show) => show switch
    {
        MoveAndLabelShow.NotDone => "Not done yet",
        MoveAndLabelShow.Done => "Done",
        _ => "All",
    };
}
