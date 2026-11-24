using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The shared label picker (Tasks redesign plan item 3, checklist 3.48-3.67; Controls/LabelPicker): a rail of categories (ADR 93 label groups,
/// then "Other labels"), the picked category's labels on the right, a search over every category, "No label", and per-label tags
/// "Current" / "N have it" for the tasks being changed. It only holds a pick (<see cref="Pick"/>, <see cref="PickChanged"/>); the caller applies
/// it (Move and label, the editor's label pill, Settings › Labels, the checklist, Goals). Built from the labels this device shows, in their order.
/// </summary>
public sealed partial class LabelPickerViewModel : ObservableObject
{
    /// <summary>The rail's last row: labels with no category, or whose category is missing or deleted (never rewritten).</summary>
    public const string OtherLabelsName = "Other";

    // "Other labels" has no icon of its own in the spec: the app's Label icon (ADR 32; Group by Labels and the Label menu use it too).
    private const MaterialIconKind OtherLabelsIcon = MaterialIconKind.CircleSlice8;

    private readonly IReadOnlyList<LabelPickerItemViewModel> _allItems;
    private LabelPickerCategoryViewModel? _shown;

    /// <param name="labels">The live labels this device shows, in the list's order (<see cref="TaskLabelService.Labels"/>).</param>
    /// <param name="groups">The live label groups, in their order (<see cref="TaskLabelService.Groups"/>).</param>
    /// <param name="currentLabels">Each task being changed: its label now (null for none). Drives the tags and the first category shown.</param>
    /// <param name="initial">The pick to start with (nothing by default).</param>
    public LabelPickerViewModel(IReadOnlyList<TaskLabel> labels, IReadOnlyList<TaskLabelGroup> groups, IReadOnlyList<Guid?> currentLabels, LabelPick initial = default)
    {
        IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(groups);
        var byGroup = new Dictionary<Guid, List<LabelPickerItemViewModel>>();
        var others = new List<LabelPickerItemViewModel>();
        var all = new List<LabelPickerItemViewModel>();
        foreach (TaskLabel label in labels.Where(label => label.IsLive))
        {
            TaskLabelGroup? group = TaskLabelGroupBook.GroupOf(label, liveGroups);
            var item = new LabelPickerItemViewModel(
                label.Id, label.Name, TaskLabelShortNames.Of(label.Name, group?.Name), group?.Name ?? string.Empty, ColorOf(label.Color),
                TagFor(label.Id, currentLabels), Toggle);
            all.Add(item);
            if (group is null)
            {
                others.Add(item);
            }
            else
            {
                if (!byGroup.TryGetValue(group.Id, out List<LabelPickerItemViewModel>? list))
                {
                    byGroup[group.Id] = list = [];
                }

                list.Add(item);
            }
        }

        var rail = groups.Where(group => group.IsLive)
            .Select(group => new LabelPickerCategoryViewModel(
                group.Id, group.Name, LabelGroupIconKinds.Of(group.Icon), byGroup.TryGetValue(group.Id, out List<LabelPickerItemViewModel>? list) ? list : [], SelectCategory))
            .ToList();
        if (others.Count > 0)
        {
            rail.Add(new LabelPickerCategoryViewModel(null, OtherLabelsName, OtherLabelsIcon, others, SelectCategory));
        }

        _allItems = all;
        Rail = rail;
        Sections = [];
        SearchText = string.Empty;
        Pick = initial;
        ApplyPick();
        Show(FirstCategory(currentLabels));
    }

    /// <summary>The pick changed (a label, "No label", or nothing after a second click).</summary>
    public event EventHandler? PickChanged;

    /// <summary>"+ Category" at the foot of the rail: the caller opens its category editor (Settings › Labels); hidden when null.</summary>
    public Action? AddCategory { get; init; }

    public bool CanAddCategory => AddCategory is not null;

    public IReadOnlyList<LabelPickerCategoryViewModel> Rail { get; }

    /// <summary>What the label pane lists: the shown category, or while searching every category with a match.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<LabelPickerSectionViewModel> Sections { get; private set; }

    /// <summary>"Type to find a label in any category · Enter picks".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    public partial string SearchText { get; set; }

    public bool IsSearching => Query.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoLabelPicked))]
    public partial LabelPick Pick { get; set; }

    public bool IsNoLabelPicked => Pick.IsNoLabel;

    private string Query => (SearchText ?? string.Empty).Trim();

    /// <summary>"No label": picks it, or takes the pick back when it was already picked.</summary>
    [RelayCommand]
    private void PickNoLabel()
    {
        Pick = Pick.IsNoLabel ? LabelPick.Nothing : LabelPick.NoLabel;
    }

    [RelayCommand]
    private void RequestAddCategory() => AddCategory?.Invoke();

    /// <summary>Enter in the search: picks the first match, shows its category and clears the text. False when nothing matches.</summary>
    public bool PickFirstMatch()
    {
        string query = Query;
        LabelPickerItemViewModel? first = query.Length == 0 ? null : Rail.SelectMany(category => category.Labels).FirstOrDefault(item => item.Matches(query));
        if (first is null)
        {
            return false;
        }

        Pick = LabelPick.Of(first.LabelId);
        LabelPickerCategoryViewModel? home = CategoryOf(first.LabelId);
        SearchText = string.Empty;
        Show(home);
        return true;
    }

    /// <summary>A rail row: clears the search and shows that category.</summary>
    public void SelectCategory(LabelPickerCategoryViewModel category)
    {
        SearchText = string.Empty;
        Show(category);
    }

    /// <summary>The category the pane shows (null while searching).</summary>
    public LabelPickerCategoryViewModel? ShownCategory => IsSearching ? null : _shown;

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnPickChanged(LabelPick value)
    {
        ApplyPick();
        PickChanged?.Invoke(this, EventArgs.Empty);
    }

    // A click on a label picks it; a click on the picked one takes the pick back (checklist 3.59).
    private void Toggle(LabelPickerItemViewModel item)
    {
        Pick = Pick.LabelId == item.LabelId ? LabelPick.Nothing : LabelPick.Of(item.LabelId);
    }

    private void ApplyPick()
    {
        foreach (LabelPickerItemViewModel item in _allItems)
        {
            item.IsPicked = Pick.LabelId == item.LabelId;
        }

        LabelPickerCategoryViewModel? holder = Pick.LabelId is Guid id ? CategoryOf(id) : null;
        foreach (LabelPickerCategoryViewModel category in Rail)
        {
            category.HasPick = ReferenceEquals(category, holder);
        }
    }

    private void Show(LabelPickerCategoryViewModel? category)
    {
        _shown = category ?? Rail.FirstOrDefault();
        Refresh();
    }

    private void Refresh()
    {
        string query = Query;
        foreach (LabelPickerCategoryViewModel category in Rail)
        {
            category.IsSelected = query.Length == 0 && ReferenceEquals(category, _shown);
            category.IsDimmed = query.Length > 0 && !category.Labels.Any(item => item.Matches(query));
        }

        if (query.Length == 0)
        {
            Sections = _shown is null ? [] : [new LabelPickerSectionViewModel(_shown.Name, _shown.Icon, _shown.Labels)];
        }
        else
        {
            Sections = Rail
                .Select(category => new LabelPickerSectionViewModel(category.Name, category.Icon, category.Labels.Where(item => item.Matches(query)).ToList()))
                .Where(section => section.Items.Count > 0)
                .ToList();
        }

        OnPropertyChanged(nameof(ShownCategory));
    }

    // The picked label's category, else the first task's label's category, else the first category (checklist 3.64).
    private LabelPickerCategoryViewModel? FirstCategory(IReadOnlyList<Guid?> currentLabels)
    {
        if (Pick.LabelId is Guid picked && CategoryOf(picked) is { } pickedHome)
        {
            return pickedHome;
        }

        if (currentLabels.Count > 0 && currentLabels[0] is Guid first && CategoryOf(first) is { } firstHome)
        {
            return firstHome;
        }

        return Rail.FirstOrDefault();
    }

    private LabelPickerCategoryViewModel? CategoryOf(Guid labelId)
    {
        return Rail.FirstOrDefault(category => category.Labels.Any(item => item.LabelId == labelId));
    }

    // "Current" when every task being changed has the label, "N have it" when only some do (SPEC wins over the mockup's "1 of 2").
    private static string TagFor(Guid labelId, IReadOnlyList<Guid?> currentLabels)
    {
        int having = currentLabels.Count(current => current == labelId);
        if (having == 0)
        {
            return string.Empty;
        }

        return having == currentLabels.Count ? "Current" : $"{having} have it";
    }

    private static Color ColorOf(string color)
    {
        return Color.TryParse(TaskLabelColors.Opaque(color), out Color parsed) ? parsed : Colors.Transparent;
    }
}
