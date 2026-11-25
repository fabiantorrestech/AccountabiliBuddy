using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Tasks;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Search filters panel (Tasks redesign item 7; SPEC "Search and filters"; boards 11, 12), over the Shelf's search or the Archive's:
/// Look in (Task names, Steps, Notes, Tags), Bins (the Shelf only: Next, To sort, Later, Today), Labels by category (chips cycle
/// ✓ include → ✕ leave out → any; a label lit in the label row shows ✓), Tags (the same; a "#tag" typed in the box shows ✓), When (Any time,
/// Today, This week, This month, Custom with From and To), then Reset and Done. Every click applies at once (7.34); the state is this PC's
/// search state, held by the host (<see cref="ISearchFiltersHost"/>), never stored in a view or synced.
/// </summary>
public sealed partial class SearchFiltersViewModel : ObservableObject
{
    private const string OtherLabelsName = "Other labels";
    private const MaterialIconKind OtherLabelsIcon = MaterialIconKind.CircleSlice8;

    private static readonly (TaskSearchWhen When, string Key, string Name)[] WhenNames =
    [
        (TaskSearchWhen.AnyTime, "any", "Any time"),
        (TaskSearchWhen.Today, "today", "Today"),
        (TaskSearchWhen.ThisWeek, "week", "This week"),
        (TaskSearchWhen.ThisMonth, "month", "This month"),
        (TaskSearchWhen.Custom, "custom", "Custom"),
    ];

    private static readonly (TaskSearchBin Bin, string Name)[] BinNames =
    [
        (TaskSearchBin.Next, "Next"),
        (TaskSearchBin.Sort, "To sort"),
        (TaskSearchBin.Later, "Later"),
        (TaskSearchBin.Today, "Today"),
    ];

    private readonly ISearchFiltersHost _host;
    private bool _syncingDates;

    /// <param name="place">"Shelf" or "Archive": the header's right-hand word.</param>
    /// <param name="showsBins">The Bins section shows (the Shelf only, 7.16-7.17).</param>
    public SearchFiltersViewModel(ISearchFiltersHost host, string place, bool showsBins)
    {
        _host = host;
        Place = place;
        ShowsBins = showsBins;
        LookIn =
        [
            new SearchFilterToggleViewModel("names", "Task names", true, ToggleLookIn),
            new SearchFilterToggleViewModel("steps", "Steps", true, ToggleLookIn),
            new SearchFilterToggleViewModel("notes", "Notes", true, ToggleLookIn),
            new SearchFilterToggleViewModel("tags", "Tags", true, ToggleLookIn),
        ];
        Bins = BinNames.Select(bin => new SearchFilterToggleViewModel(bin.Bin.ToString(), bin.Name, true, ToggleBin)).ToList();
        WhenChoices = WhenNames.Select(when => new SearchFilterToggleViewModel(when.Key, when.Name, false, PickWhen)).ToList();
        ShowState();
    }

    /// <summary>"Shelf" or "Archive".</summary>
    public string Place { get; }

    public bool ShowsBins { get; }

    public IReadOnlyList<SearchFilterToggleViewModel> LookIn { get; }

    public IReadOnlyList<SearchFilterToggleViewModel> Bins { get; }

    /// <summary>The labels by category (the task list's order), "Other labels" last; rebuilt each time the panel opens.</summary>
    public ObservableCollection<SearchFilterLabelGroupViewModel> LabelGroups { get; } = [];

    /// <summary>One chip per tag in use, A–Z (7.24).</summary>
    public ObservableCollection<SearchFilterPickChipViewModel> Tags { get; } = [];

    public bool HasLabels => LabelGroups.Count > 0;

    public bool HasTags => Tags.Count > 0;

    public IReadOnlyList<SearchFilterToggleViewModel> WhenChoices { get; }

    /// <summary>"Custom" is picked: the From and To dates show (7.30).</summary>
    [ObservableProperty]
    public partial bool IsCustom { get; set; }

    /// <summary>Custom's From date (a CalendarDatePicker's value; the time part is unused).</summary>
    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    /// <summary>Custom's To date.</summary>
    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    /// <summary>Done or a click outside: the host closes the panel.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The panel opens: its label and tag chips are built from the task list as it is now, then every chip shows the state.</summary>
    public void Rebuild()
    {
        LabelGroups.Clear();
        Tags.Clear();
        if (_host.SearchStore is { IsActive: true } store)
        {
            foreach (SearchFilterLabelGroupViewModel group in GroupsOf(store))
            {
                LabelGroups.Add(group);
            }

            foreach (string tag in store.VisibleTags().Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                Tags.Add(new SearchFilterPickChipViewModel(null, tag, "#" + tag, null, CycleTag));
            }
        }

        OnPropertyChanged(nameof(HasLabels));
        OnPropertyChanged(nameof(HasTags));
        ShowState();
    }

    /// <summary>Every chip, the When choice and the dates from the host's filters, words and label row (also after the words change).</summary>
    public void ShowState()
    {
        TaskSearchFilters filters = _host.SearchFilters;
        LookIn[0].IsOn = filters.InNames;
        LookIn[1].IsOn = filters.InSteps;
        LookIn[2].IsOn = filters.InNotes;
        LookIn[3].IsOn = filters.InTags;
        for (int index = 0; index < Bins.Count; index++)
        {
            Bins[index].IsOn = filters.HasBin(BinNames[index].Bin);
        }

        foreach (SearchFilterPickChipViewModel chip in LabelGroups.SelectMany(group => group.Chips))
        {
            chip.Pick = LabelState(chip.LabelId, filters);
        }

        HashSet<string> typed = TaskSearch.TagWords(_host.SearchWords).ToHashSet(StringComparer.Ordinal);
        foreach (SearchFilterPickChipViewModel chip in Tags)
        {
            chip.Pick = TagState(chip.Tag!, filters, typed);
        }

        for (int index = 0; index < WhenChoices.Count; index++)
        {
            WhenChoices[index].IsOn = WhenNames[index].When == filters.When;
        }

        IsCustom = filters.When == TaskSearchWhen.Custom;
        _syncingDates = true;
        FromDate = filters.From?.ToDateTime(TimeOnly.MinValue);
        ToDate = filters.To?.ToDateTime(TimeOnly.MinValue);
        _syncingDates = false;
    }

    /// <summary>"Reset" (7.33): every Look in and Bins chip on, every label and tag chip to any, When back to Any time.</summary>
    [RelayCommand]
    private void Reset()
    {
        // The typed "#tag" words and the label row's picks are what their chips show: they go too, so every chip reads any.
        string words = _host.SearchWords;
        foreach (string tag in TaskSearch.TagWords(words))
        {
            words = TaskSearch.WithoutTagWord(words, tag);
        }

        if (words != _host.SearchWords)
        {
            _host.SearchWords = words;
        }

        foreach (SearchFilterPickChipViewModel chip in LabelGroups.SelectMany(group => group.Chips).Where(chip => InRow(chip.LabelId)).ToList())
        {
            _host.TakeOffRow(chip.LabelId);
        }

        _host.SearchFilters = TaskSearchFilters.Default;
        ShowState();
    }

    /// <summary>"Done" (7.34): closes the panel; the filters already apply.</summary>
    [RelayCommand]
    private void Done() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnFromDateChanged(DateTime? value) => ApplyDates();

    partial void OnToDateChanged(DateTime? value) => ApplyDates();

    private void ApplyDates()
    {
        if (_syncingDates)
        {
            return;
        }

        DateOnly? from = FromDate is DateTime start ? DateOnly.FromDateTime(start) : null;
        DateOnly? to = ToDate is DateTime end ? DateOnly.FromDateTime(end) : null;
        _host.SearchFilters = _host.SearchFilters with { When = TaskSearchWhen.Custom, From = from, To = to };
    }

    private void ToggleLookIn(SearchFilterToggleViewModel chip)
    {
        TaskSearchFilters filters = _host.SearchFilters;
        _host.SearchFilters = chip.Key switch
        {
            "names" => filters with { InNames = !filters.InNames },
            "steps" => filters with { InSteps = !filters.InSteps },
            "notes" => filters with { InNotes = !filters.InNotes },
            _ => filters with { InTags = !filters.InTags },
        };
        ShowState();
    }

    private void ToggleBin(SearchFilterToggleViewModel chip)
    {
        TaskSearchBin bin = Enum.Parse<TaskSearchBin>(chip.Key);
        _host.SearchFilters = _host.SearchFilters.WithBin(bin, !_host.SearchFilters.HasBin(bin));
        ShowState();
    }

    private void PickWhen(SearchFilterToggleViewModel chip)
    {
        TaskSearchWhen when = WhenNames.First(name => name.Key == chip.Key).When;
        _host.SearchFilters = _host.SearchFilters with { When = when };
        ShowState();
    }

    // A label chip's click (7.21, 7.23): the next state of what it shows. A label lit in the label row shows ✓; its next state (✕) is kept
    // here and the label leaves the row.
    private void CycleLabel(SearchFilterPickChipViewModel chip)
    {
        TaskSearchFilters filters = _host.SearchFilters;
        TaskSearchPick next = TaskSearchFilters.Cycle(LabelState(chip.LabelId, filters));
        if (InRow(chip.LabelId))
        {
            _host.TakeOffRow(chip.LabelId);
        }

        _host.SearchFilters = chip.LabelId is Guid id ? filters.WithLabel(id, next) : filters with { NoLabel = next };
        ShowState();
    }

    // A tag chip's click (7.25-7.27): the next state of what it shows. A tag typed as "#tag" shows ✓; its next state (✕) is kept here and
    // the word leaves the box.
    private void CycleTag(SearchFilterPickChipViewModel chip)
    {
        string tag = chip.Tag!;
        TaskSearchFilters filters = _host.SearchFilters;
        HashSet<string> typed = TaskSearch.TagWords(_host.SearchWords).ToHashSet(StringComparer.Ordinal);
        TaskSearchPick next = TaskSearchFilters.Cycle(TagState(tag, filters, typed));
        if (typed.Contains(TaskSearchFilters.TagKey(tag)))
        {
            _host.SearchWords = TaskSearch.WithoutTagWord(_host.SearchWords, tag);
        }

        _host.SearchFilters = filters.WithTag(tag, next);
        ShowState();
    }

    private bool InRow(Guid? labelId) => labelId is Guid id ? _host.IsLabelInRow(id) : _host.IsNoLabelInRow;

    // What a label chip shows: its pick here, else ✓ while the label is lit in the row.
    private TaskSearchPick LabelState(Guid? labelId, TaskSearchFilters filters)
    {
        TaskSearchPick pick = labelId is Guid id ? filters.LabelPick(id) : filters.NoLabel;
        return pick == TaskSearchPick.Any && InRow(labelId) ? TaskSearchPick.Include : pick;
    }

    // What a tag chip shows: its pick here, else ✓ while "#tag" is typed in the box.
    private static TaskSearchPick TagState(string tag, TaskSearchFilters filters, HashSet<string> typed)
    {
        TaskSearchPick pick = filters.TagPick(tag);
        return pick == TaskSearchPick.Any && typed.Contains(TaskSearchFilters.TagKey(tag)) ? TaskSearchPick.Include : pick;
    }

    // The labels this PC shows under their categories (the categories' order, each one's labels in the list's order; a category with
    // none shown is left out), then "Other labels" with the labels without one and "No label".
    private List<SearchFilterLabelGroupViewModel> GroupsOf(TaskStore store)
    {
        IReadOnlyList<TaskLabelGroup> groups = TaskLabelGroupBook.Live(store.Lists.LabelGroups);
        IReadOnlyDictionary<Guid, TaskLabelGroup> liveGroups = TaskLabelGroupBook.LiveById(groups);
        var byGroup = new Dictionary<Guid, List<SearchFilterPickChipViewModel>>();
        var others = new List<SearchFilterPickChipViewModel>();
        foreach (TaskLabel label in store.VisibleLabels)
        {
            TaskLabelGroup? group = TaskLabelGroupBook.GroupOf(label, liveGroups);
            Color color = Color.TryParse(TaskLabelColors.Opaque(label.Color), out Color parsed) ? parsed : Colors.Gray;
            var chip = new SearchFilterPickChipViewModel(
                label.Id, null, TaskLabelShortNames.Of(label.Name, group?.Name), new SolidColorBrush(color), CycleLabel);
            if (group is null)
            {
                others.Add(chip);
            }
            else
            {
                if (!byGroup.TryGetValue(group.Id, out List<SearchFilterPickChipViewModel>? chips))
                {
                    byGroup[group.Id] = chips = [];
                }

                chips.Add(chip);
            }
        }

        var shown = new List<SearchFilterLabelGroupViewModel>();
        foreach (TaskLabelGroup group in groups)
        {
            if (byGroup.TryGetValue(group.Id, out List<SearchFilterPickChipViewModel>? chips))
            {
                shown.Add(new SearchFilterLabelGroupViewModel(group.Name, LabelGroupIconKinds.Of(group.Icon), chips));
            }
        }

        others.Add(new SearchFilterPickChipViewModel(null, null, "No label", null, CycleLabel));
        shown.Add(new SearchFilterLabelGroupViewModel(OtherLabelsName, OtherLabelsIcon, others));
        return shown;
    }
}
