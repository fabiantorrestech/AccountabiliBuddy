using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// An add box (spec 03 §B "Add box", B2 "Jot it down", 05 "Add box", Implementation Notes 13x): the typed title, where it goes (Send
/// to, To sort by default), the "@label" / "#tag" list that opens as soon as "@" or "#" is typed and narrows per letter, the picked
/// label and tags as removable chips, and the title help under the box: the 60-character soft limit with its counter from 50, the
/// "Move … to notes" split, the project marker at the start ("Part of a project?" with label and tag choices, Apply, Keep as typed)
/// and the one-time offer to learn a marker style. Rule-based and local; nothing here blocks adding. One class for the Today column's
/// box, a Shelf bin's "+" box and Jot it down (<see cref="AddBoxKind"/>); the page adds the task (<see cref="IAddBoxHost"/>).
/// </summary>
public sealed partial class AddBoxViewModel : ObservableObject
{
    private const int SplitTailShown = 18;

    private static readonly Regex Typing = new(@"(^|\s)([@#])([\w-]*)$", RegexOptions.Compiled);
    private static readonly Regex Finished = new(@"(^|\s)([@#])([\w-]+)\s$", RegexOptions.Compiled);

    private readonly IAddBoxHost _host;
    private readonly TaskBin _presetDestination;

    // The picked chips: one label, any number of tags.
    private Guid? _labelId;
    private readonly List<string> _tags = [];

    // The "@" or "#" word being typed: where it starts (the "@") and ends (the caret).
    private int _suggestStart = -1;
    private int _suggestEnd = -1;

    // The split as moved with Alt+←/→, for the title it was moved on.
    private TaskTitleSplit? _movedSplit;
    private string _movedSplitFor = string.Empty;

    // The marker's label and tag as changed in its dropdowns, for the marker they were changed on.
    private string _markerRaw = string.Empty;
    private bool _markerLabelChosen;
    private Guid? _markerLabel;
    private bool _markerTagChosen;
    private string? _markerTag;

    private TaskTitleAnalysis? _analysis;
    private TaskTypedTitle? _parsed;
    private ProjectMarkerCandidate? _offer;
    private bool _offerAsked;

    // Jot it down: how many were jotted today (this run of the app).
    private int _jotted;
    private DateOnly _jottedOn;

    public AddBoxViewModel(IAddBoxHost host, AddBoxKind kind, TaskBin destination = TaskBin.Sort)
    {
        _host = host;
        Kind = kind;
        _presetDestination = destination;
        Destination = destination;
        Text = string.Empty;
    }

    public AddBoxKind Kind { get; }

    /// <summary>The bin a Shelf bin's box starts with (To sort for the others).</summary>
    public TaskBin PresetDestination => _presetDestination;

    public bool IsJot => Kind == AddBoxKind.Jot;

    public bool HasTitleHelp => Kind != AddBoxKind.Jot;

    /// <summary>Raised when the box wants the keyboard (Ctrl+J, a Shelf bin's "+", after Apply).</summary>
    public event EventHandler? FocusRequested;

    /// <summary>Raised when the box changed its own text and the caret should go to this place.</summary>
    public event EventHandler<int>? CaretRequested;

    /// <summary>Raised when a Shelf bin's box or the jot closes (Esc, or left empty).</summary>
    public event EventHandler? Closed;

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>The keyboard is in the box (or one of its menus is open): the full row with Send to; otherwise a pill.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public bool IsExpanded => IsFocused;

    /// <summary>A short "+ Add a task" pill: not in use and nothing typed.</summary>
    public bool IsResting => !IsFocused && Text.Length == 0;

    /// <summary>Text left in the box after leaving it: greyed, the box shrunk to fit it.</summary>
    public bool IsLeftover => !IsFocused && Text.Length > 0;

    /// <summary>Where the task goes (This session through the session's cap).</summary>
    [ObservableProperty]
    public partial TaskBin Destination { get; set; }

    /// <summary>The send button: "Add to To sort".</summary>
    public string DestinationText => "Add to " + TaskBins.Name(Destination);

    public MaterialIconKind DestinationIcon => IconOf(Destination);

    /// <summary>"Keep as typed": no marker or split suggestion for this task (until the box is emptied).</summary>
    [ObservableProperty]
    public partial bool KeepAsTyped { get; set; }

    // ---- Jot it down ----

    /// <summary>The jot is open (its box shows instead of the faint pill).</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>A session runs: the jot's pill is fainter.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillOpacity))]
    public partial bool IsQuiet { get; set; }

    public double PillOpacity => IsQuiet ? 0.45 : 0.8;

    /// <summary>"· 4 today"; empty before the first jot of the day.</summary>
    public string JotCountText => _jotted > 0 && _jottedOn == DateOnly.FromDateTime(DateTime.Now) ? "· " + _jotted + " today" : string.Empty;

    // ---- chips ----

    public ObservableCollection<AddBoxChipViewModel> Chips { get; } = [];

    public bool HasChips => Chips.Count > 0;

    // ---- the "@" / "#" list ----

    public ObservableCollection<AddBoxSuggestionViewModel> Suggestions { get; } = [];

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>"Labels" or "Tags".</summary>
    [ObservableProperty]
    public partial string SuggestionHead { get; set; } = string.Empty;

    // ---- the hint line under the box ----

    public bool ShowsCounter => HasTitleHelp && _analysis is { ShowCounter: true };

    /// <summary>"54 / 60".</summary>
    public string CounterText => _analysis is null ? string.Empty : _analysis.Length + " / " + TaskTitleHelp.SoftLimit;

    public bool IsOverLimit => _analysis is { OverSoftLimit: true };

    public bool ShowsMarker => HasTitleHelp && _analysis?.Marker is not null && Text.Length > 3;

    public bool ShowsSplit => HasTitleHelp && !ShowsMarker && CurrentSplit is not null;

    /// <summary>"Move “handle long titles…” to notes".</summary>
    public string SplitButtonText => CurrentSplit is { } split ? "Move “" + Short(split.Tail, SplitTailShown) + "” to notes" : string.Empty;

    /// <summary>"Title becomes “…”" (also says Alt+←/→ moves the split).</summary>
    public string SplitTip => CurrentSplit is { } split ? "Title becomes “" + split.Head + "” · Alt+← / Alt+→ moves the split by a word" : string.Empty;

    public IBrush MarkerLabelDot => LabelOf(MarkerLabel) is { } label ? BrushOf(label) : Brushes.Transparent;

    public bool HasMarkerLabel => LabelOf(MarkerLabel) is not null;

    /// <summary>"Label AI" or "Pick a label".</summary>
    public string MarkerLabelText => LabelOf(MarkerLabel) is { } label ? "Label " + label.Name : "Pick a label";

    /// <summary>"#mobile" or "No tag".</summary>
    public string MarkerTagText => MarkerTag is { } tag ? "#" + tag : "No tag";

    public bool ShowsOffer => HasTitleHelp && _offer is not null && !ShowsCounter && !ShowsMarker && !ShowsSplit && !HasChips;

    /// <summary>"Treat “⟦Name⟧” as a project marker?".</summary>
    public string OfferText => _offer is { } offer ? "Treat “" + offer.Pattern.Open + "Name" + offer.Pattern.Close + "” as a project marker?" : string.Empty;

    public bool ShowsChipLine => HasChips && !ShowsMarker && !ShowsSplit;

    /// <summary>The hint line shows (it floats under the box, so nothing moves when it does).</summary>
    public bool ShowsHint => IsFocused && (ShowsCounter || ShowsMarker || ShowsSplit || ShowsChipLine || ShowsOffer);

    private TaskTitleSplit? CurrentSplit
    {
        get
        {
            if (_analysis?.Split is not { } split || _parsed is null)
            {
                return null;
            }

            return _movedSplit is not null && _movedSplitFor == _parsed.Title ? _movedSplit : split;
        }
    }

    private Guid? MarkerLabel => _markerLabelChosen ? _markerLabel : _analysis?.Marker?.LabelId;

    private string? MarkerTag => _markerTagChosen ? _markerTag : _analysis?.Marker?.Tag;

    // ---- what the view calls ----

    /// <summary>The keyboard came into the box or left it.</summary>
    public void SetFocused(bool focused)
    {
        IsFocused = focused;
        if (focused && !_offerAsked && HasTitleHelp)
        {
            // Once per box and run: the learner reads the task list, so not on every keystroke.
            _offerAsked = true;
            _offer = _host.MarkerToOffer();
            RaiseHint();
        }

        if (!focused)
        {
            DismissSuggestions();
        }
    }

    /// <summary>The text or the caret changed: a finished "@name " or "#tag " becomes a chip, and the "@" / "#" list follows the word at the caret.</summary>
    public void Typed(int caret)
    {
        caret = Math.Clamp(caret, 0, Text.Length);
        string before = Text[..caret];
        Match finished = Finished.Match(before);
        if (finished.Success && TryChip(finished.Groups[2].Value[0], finished.Groups[3].Value))
        {
            // The word leaves the text; the space before it stays (or none at the start).
            int start = finished.Index + finished.Groups[1].Length;
            string kept = Text[..start] + Text[caret..].TrimStart();
            Text = kept;
            DismissSuggestions();
            CaretRequested?.Invoke(this, start);
            return;
        }

        Match typing = Typing.Match(before);
        if (!typing.Success)
        {
            DismissSuggestions();
            return;
        }

        _suggestStart = typing.Index + typing.Groups[1].Length;
        _suggestEnd = caret;
        FillSuggestions(typing.Groups[2].Value[0], typing.Groups[3].Value);
    }

    /// <summary>↑/↓ in the "@" / "#" list; false when it isn't open.</summary>
    public bool MoveSuggestion(int step)
    {
        if (!HasSuggestions)
        {
            return false;
        }

        int index = Suggestions.ToList().FindIndex(item => item.IsSelected);
        int next = ((index < 0 ? 0 : index) + step + Suggestions.Count) % Suggestions.Count;
        for (int i = 0; i < Suggestions.Count; i++)
        {
            Suggestions[i].IsSelected = i == next;
        }

        return true;
    }

    /// <summary>Enter or Tab while the list is open: the selected entry becomes a chip; false when the list isn't open.</summary>
    public bool PickSelectedSuggestion()
    {
        if (!HasSuggestions)
        {
            return false;
        }

        Pick(Suggestions.FirstOrDefault(item => item.IsSelected) ?? Suggestions[0]);
        return true;
    }

    /// <summary>Closes the "@" / "#" list; false when it wasn't open.</summary>
    public bool DismissSuggestions()
    {
        _suggestStart = _suggestEnd = -1;
        if (!HasSuggestions)
        {
            return false;
        }

        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
        return true;
    }

    /// <summary>Backspace at the start of the box: the last chip goes (the tags first from the newest, then the label). False when there is none.</summary>
    public bool RemoveLastChip()
    {
        if (_tags.Count > 0)
        {
            _tags.RemoveAt(_tags.Count - 1);
        }
        else if (_labelId is not null)
        {
            _labelId = null;
        }
        else
        {
            return false;
        }

        RebuildChips();
        return true;
    }

    /// <summary>Alt+← / Alt+→: the split moves by a word. False when no split is suggested.</summary>
    public bool MoveSplit(int direction)
    {
        if (!ShowsSplit || CurrentSplit is not { } split || _parsed is null)
        {
            return false;
        }

        _movedSplit = TaskTitleSplitter.Moved(_parsed.Title, split, direction);
        _movedSplitFor = _parsed.Title;
        RaiseHint();
        return true;
    }

    /// <summary>Esc: closes the list first; then a Shelf bin's box or the jot closes (the jot forgets its text), the Today column's box empties.</summary>
    public void Escape()
    {
        if (DismissSuggestions())
        {
            return;
        }

        if (Kind == AddBoxKind.Main)
        {
            Reset();
            return;
        }

        Close();
    }

    /// <summary>Leaving the box: a Shelf bin's box or the jot with nothing typed closes.</summary>
    public void LeftEmpty()
    {
        if (Kind != AddBoxKind.Main && IsOpen && Text.Trim().Length == 0 && Chips.Count == 0)
        {
            Close();
        }
    }

    /// <summary>Opens the box and gives it the keyboard (Ctrl+J, the jot's pill, a Shelf bin's "+").</summary>
    [RelayCommand]
    public void Open()
    {
        IsOpen = true;
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---- adding ----

    /// <summary>Enter or the send button: the task is added with its chips (and "@label" / "#tag" typed in the text).</summary>
    [RelayCommand]
    private async Task SubmitAsync()
    {
        DismissSuggestions();
        TaskTypedTitle parsed = TaskTitleHelp.Parse(Text, _host.Labels);
        if (parsed.Title.Length == 0)
        {
            return;
        }

        await AddAsync(parsed.Title, parsed, note: null);
    }

    /// <summary>"Move “…” to notes": the title is the part before the split, the rest its note.</summary>
    [RelayCommand]
    private async Task AcceptSplitAsync()
    {
        if (CurrentSplit is not { } split || _parsed is not { } parsed)
        {
            return;
        }

        string tail = split.Tail.Trim();
        string note = tail.Length == 0 ? tail : char.ToUpper(tail[0], CultureInfo.CurrentCulture) + tail[1..];
        await AddAsync(split.Head, parsed, note);
    }

    private async Task AddAsync(string title, TaskTypedTitle parsed, string? note)
    {
        TaskBin destination = IsJot ? TaskBin.Sort : Destination;
        var request = new AddBoxRequest(title, destination, _labelId ?? parsed.LabelId, TaskLabels.With(_tags, parsed.Tags) ?? [], note);
        if (!await _host.AddFromBoxAsync(request))
        {
            return;
        }

        FocusRequested?.Invoke(this, EventArgs.Empty);
        if (IsJot)
        {
            DateOnly today = DateOnly.FromDateTime(DateTime.Now);
            _jotted = _jottedOn == today ? _jotted + 1 : 1;
            _jottedOn = today;
            OnPropertyChanged(nameof(JotCountText));
        }

        Reset();
    }

    // ---- Send to ----

    /// <summary>The Send to menu: "New tasks go to" This session, Queued, To sort (the default), Next, Later; then From your lists….</summary>
    public IReadOnlyList<TaskMenuEntry> DestinationMenu()
    {
        var entries = new List<TaskMenuEntry> { new("New tasks go to", IsHeading: true) };
        foreach (TaskBin bin in new[] { TaskBin.Session, TaskBin.Queued, TaskBin.Sort, TaskBin.Next, TaskBin.Later })
        {
            TaskBin chosen = bin;
            string header = bin == TaskBin.Sort ? TaskBins.Name(bin) + " · Default" : TaskBins.Name(bin);
            entries.Add(new TaskMenuEntry(header, new RelayCommand(() => Destination = chosen), IsChecked: Destination == bin, Icon: IconOf(bin)));
        }

        if (_host.FromYourLists is not null)
        {
            entries.Add(TaskMenuEntry.Separator);
            entries.Add(TaskMenuEntry.View("From your lists…", "fromLists", MaterialIconKind.ViewListOutline));
        }

        return entries;
    }

    public ListPickerViewModel? FromYourLists => _host.FromYourLists;

    // ---- project markers ----

    /// <summary>The marker's label dropdown: every label (the suggested one says so), then No label.</summary>
    public IReadOnlyList<TaskMenuEntry> MarkerLabelMenu()
    {
        Guid? suggested = _analysis?.Marker?.LabelId;
        Guid? current = MarkerLabel;
        var entries = new List<TaskMenuEntry> { new("Label", IsHeading: true) };
        foreach (TaskLabel label in _host.Labels)
        {
            Guid id = label.Id;
            string header = id == suggested ? label.Name + " · suggested" : label.Name;
            entries.Add(new TaskMenuEntry(header, new RelayCommand(() => ChooseMarkerLabel(id)), IsChecked: current == id, Dot: BrushOf(label)));
        }

        entries.Add(new TaskMenuEntry("No label", new RelayCommand(() => ChooseMarkerLabel(null)), IsChecked: current is null));
        return entries;
    }

    /// <summary>The marker's tag dropdown: the suggested tag, the tags in use, then No tag.</summary>
    public IReadOnlyList<TaskMenuEntry> MarkerTagMenu()
    {
        string? suggested = _analysis?.Marker?.Tag;
        string? current = MarkerTag;
        var names = new List<string>();
        if (suggested is not null)
        {
            names.Add(suggested);
        }

        names.AddRange(_host.TagCounts().Select(pair => pair.Key).Where(tag => !TaskLabels.Has(names, tag)));
        var entries = new List<TaskMenuEntry> { new("Tag", IsHeading: true) };
        foreach (string tag in names)
        {
            string chosen = tag;
            string header = "#" + tag + (string.Equals(tag, suggested, StringComparison.OrdinalIgnoreCase) ? " · suggested" : string.Empty);
            entries.Add(new TaskMenuEntry(header, new RelayCommand(() => ChooseMarkerTag(chosen)), IsChecked: string.Equals(current, tag, StringComparison.OrdinalIgnoreCase), Icon: MaterialIconKind.Pound));
        }

        entries.Add(new TaskMenuEntry("No tag", new RelayCommand(() => ChooseMarkerTag(null)), IsChecked: current is null, Icon: MaterialIconKind.Close));
        return entries;
    }

    /// <summary>Apply: the marker leaves the title; its label and tag (and any typed "@label" / "#tag") become chips.</summary>
    [RelayCommand]
    private void ApplyMarker()
    {
        if (_analysis?.Marker is not { } marker || _parsed is not { } parsed)
        {
            return;
        }

        Guid? label = MarkerLabel ?? parsed.LabelId;
        if (label is not null)
        {
            _labelId = label;
        }

        foreach (string tag in parsed.Tags.Concat(MarkerTag is { } markerTag ? [markerTag] : []))
        {
            AddTag(tag);
        }

        KeepAsTyped = true;
        Text = marker.Rest.Trim();
        RebuildChips();
        CaretRequested?.Invoke(this, Text.Length);
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void KeepTyped()
    {
        KeepAsTyped = true;
    }

    [RelayCommand]
    private async Task LearnOfferAsync()
    {
        if (_offer is { } offer)
        {
            _offer = null;
            RaiseHint();
            await _host.LearnMarkerAsync(offer.Pattern);
        }
    }

    [RelayCommand]
    private async Task DeclineOfferAsync()
    {
        if (_offer is { } offer)
        {
            _offer = null;
            RaiseHint();
            await _host.DeclineMarkerAsync(offer.Pattern);
        }
    }

    /// <summary>The page's labels or tags changed: the help and chips read them again.</summary>
    public void Refresh()
    {
        Analyze();
        RebuildChips();
    }

    // ---- inside ----

    partial void OnTextChanged(string value)
    {
        if (value.Length == 0)
        {
            KeepAsTyped = false;
        }

        Analyze();
        OnPropertyChanged(nameof(IsResting));
        OnPropertyChanged(nameof(IsLeftover));
    }

    partial void OnIsFocusedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(IsResting));
        OnPropertyChanged(nameof(IsLeftover));
        OnPropertyChanged(nameof(ShowsHint));
    }

    partial void OnDestinationChanged(TaskBin value)
    {
        OnPropertyChanged(nameof(DestinationText));
        OnPropertyChanged(nameof(DestinationIcon));
    }

    partial void OnKeepAsTypedChanged(bool value) => Analyze();

    private void Analyze()
    {
        if (!HasTitleHelp)
        {
            return;
        }

        _parsed = TaskTitleHelp.Parse(Text, _host.Labels);
        _analysis = TaskTitleHelp.Analyze(_parsed.Title, _host.Labels, _host.CustomMarkers, KeepAsTyped);
        string raw = _analysis.Marker?.Raw ?? string.Empty;
        if (raw != _markerRaw)
        {
            // A different marker: its own suggestions again.
            _markerRaw = raw;
            _markerLabelChosen = _markerTagChosen = false;
            _markerLabel = null;
            _markerTag = null;
        }

        RaiseHint();
    }

    private void RaiseHint()
    {
        OnPropertyChanged(nameof(ShowsCounter));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(IsOverLimit));
        OnPropertyChanged(nameof(ShowsMarker));
        OnPropertyChanged(nameof(ShowsSplit));
        OnPropertyChanged(nameof(SplitButtonText));
        OnPropertyChanged(nameof(SplitTip));
        OnPropertyChanged(nameof(MarkerLabelDot));
        OnPropertyChanged(nameof(HasMarkerLabel));
        OnPropertyChanged(nameof(MarkerLabelText));
        OnPropertyChanged(nameof(MarkerTagText));
        OnPropertyChanged(nameof(ShowsOffer));
        OnPropertyChanged(nameof(OfferText));
        OnPropertyChanged(nameof(ShowsChipLine));
        OnPropertyChanged(nameof(ShowsHint));
    }

    private void ChooseMarkerLabel(Guid? id)
    {
        _markerLabelChosen = true;
        _markerLabel = id;
        RaiseHint();
    }

    private void ChooseMarkerTag(string? tag)
    {
        _markerTagChosen = true;
        _markerTag = tag;
        RaiseHint();
    }

    private void FillSuggestions(char kind, string query)
    {
        Suggestions.Clear();
        if (kind == '@')
        {
            SuggestionHead = "Labels";
            IEnumerable<TaskLabel> matching = _host.Labels
                .Where(label => label.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(label => label.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase))
                .Take(6);
            foreach (TaskLabel label in matching)
            {
                Suggestions.Add(new AddBoxSuggestionViewModel(label.Name, label.Name, string.Empty, BrushOf(label), isTag: false, creates: false, Pick));
            }

            if (query.Length > 0 && !_host.Labels.Any(label => string.Equals(label.Name, query, StringComparison.OrdinalIgnoreCase)))
            {
                Suggestions.Add(new AddBoxSuggestionViewModel(query, "Create label “" + query + "”", "Enter", Brushes.Transparent, isTag: false, creates: true, Pick));
            }
        }
        else
        {
            SuggestionHead = "Tags";
            IReadOnlyList<KeyValuePair<string, int>> tags = _host.TagCounts();
            foreach (KeyValuePair<string, int> tag in tags.Where(pair => pair.Key.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(6))
            {
                string count = tag.Value == 1 ? "1 task" : tag.Value + " tasks";
                Suggestions.Add(new AddBoxSuggestionViewModel(tag.Key, tag.Key, count, null, isTag: true, creates: false, Pick));
            }

            if (query.Length > 0 && !tags.Any(pair => string.Equals(pair.Key, query, StringComparison.OrdinalIgnoreCase)))
            {
                Suggestions.Add(new AddBoxSuggestionViewModel(query, query, "new tag", null, isTag: true, creates: false, Pick));
            }
        }

        if (Suggestions.Count > 0)
        {
            Suggestions[0].IsSelected = true;
        }

        OnPropertyChanged(nameof(HasSuggestions));
    }

    // A picked entry: a chip, and the "@word" / "#word" leaves the text (the caret goes where it was).
    private void Pick(AddBoxSuggestionViewModel item)
    {
        int start = _suggestStart;
        int end = _suggestEnd;
        _ = PickAsync(item, start, end);
    }

    private async Task PickAsync(AddBoxSuggestionViewModel item, int start, int end)
    {
        DismissSuggestions();
        if (item.IsTag)
        {
            AddTag(item.Name);
        }
        else
        {
            Guid? id = item.Creates ? await _host.CreateLabelAsync(item.Name) : _host.Labels.FirstOrDefault(label => label.Name == item.Name)?.Id;
            if (id is null)
            {
                return;
            }

            _labelId = id;
        }

        if (start >= 0 && end >= start && end <= Text.Length)
        {
            Text = Text[..start] + Text[end..].TrimStart();
            CaretRequested?.Invoke(this, start);
        }

        RebuildChips();
        FocusRequested?.Invoke(this, EventArgs.Empty);
    }

    // "@name " with a known label, or "#tag ": a chip. An unknown "@word" stays text.
    private bool TryChip(char kind, string name)
    {
        if (kind == '#')
        {
            AddTag(name);
            RebuildChips();
            return true;
        }

        TaskLabel? label = _host.Labels.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        if (label is null)
        {
            return false;
        }

        _labelId = label.Id;
        RebuildChips();
        return true;
    }

    private void AddTag(string tag)
    {
        if (TaskLabels.Clean([tag]) is [string clean] && !TaskLabels.Has(_tags, clean))
        {
            _tags.Add(clean);
        }
    }

    private void RebuildChips()
    {
        Chips.Clear();
        if (LabelOf(_labelId) is { } label)
        {
            Chips.Add(new AddBoxChipViewModel(label.Name, BrushOf(label), () =>
            {
                _labelId = null;
                RebuildChips();
            }));
        }
        else
        {
            _labelId = null;
        }

        foreach (string tag in _tags)
        {
            string chosen = tag;
            Chips.Add(new AddBoxChipViewModel("#" + tag, null, () =>
            {
                _tags.RemoveAll(held => string.Equals(held, chosen, StringComparison.OrdinalIgnoreCase));
                RebuildChips();
            }));
        }

        OnPropertyChanged(nameof(HasChips));
        RaiseHint();
    }

    private void Reset()
    {
        DismissSuggestions();
        _labelId = null;
        _tags.Clear();
        _movedSplit = null;
        _movedSplitFor = string.Empty;
        Text = string.Empty;
        KeepAsTyped = false;
        if (Kind == AddBoxKind.Bin)
        {
            Destination = _presetDestination;
        }

        RebuildChips();
    }

    private void Close()
    {
        Reset();
        IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private TaskLabel? LabelOf(Guid? id)
    {
        return id is Guid wanted ? _host.Labels.FirstOrDefault(label => label.Id == wanted) : null;
    }

    private static IBrush BrushOf(TaskLabel label)
    {
        return TaskCategoryBrushes.Of(TaskLabelColors.Opaque(label.Color));
    }

    private static MaterialIconKind IconOf(TaskBin bin)
    {
        return bin switch
        {
            TaskBin.Session => MaterialIconKind.PlayCircleOutline,
            TaskBin.Queued => MaterialIconKind.PlaylistPlus,
            _ => MaterialIconKind.Tray,
        };
    }

    private static string Short(string text, int length)
    {
        string trimmed = text.Trim();
        return trimmed.Length > length ? trimmed[..length].TrimEnd() + "…" : trimmed;
    }
}
