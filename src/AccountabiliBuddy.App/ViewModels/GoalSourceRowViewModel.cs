using System.Collections.ObjectModel;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One row of Edit goal › Tracking &amp; sources › "Counting now" (round 2 R5.7, board Goals-PC-Edit): what the source is, its setting
/// (the label, the #tag, the calendar category, the app, the Beeminder goal and its direction: Read only; Send and Both "Coming soon"
/// until ADR 80), and its switch (counts by itself, brief §5.10). Phone-only sources say "Measured on your phone"; since ADR 79 (GS3a)
/// their synced day values count here once the phone sends them, and a Beeminder link on a PC without a token says its values come
/// from the PC that reads Beeminder. Screen time (ST2) is measured on this PC too: it lists the apps and sites that count (a kind chip each,
/// guessed as you type), a pick list of what this PC saw in the last 30 days, and the devices that count (this PC, then the group's others).
/// Deep work's one row is locked. SR1-P2: the steps row says which steps count (All, Watch, Phone; ADR 91) and shares that choice with the two
/// steps tiles; a screen-time entry has its own devices chip (ST4), a line per ticked device shows what counts there, and the pick lists are this
/// PC's own history and the entries already in the goals.
/// <see cref="ToRule"/> gives the stored rule back.
/// </summary>
public sealed partial class GoalSourceRowViewModel : ObservableObject
{
    private readonly GoalSourceRule _rule;
    private readonly Action<GoalSourceRowViewModel> _remove;
    private readonly Action _changed;
    private readonly bool _beeminderConnected;
    private readonly bool _groupHasPhone;
    private readonly bool _inSyncGroup;
    private readonly bool _hasAndroidDevice;
    private GoalScreenTimeSeen _seen = GoalScreenTimeSeen.Empty;
    private readonly Guid _selfDeviceId;
    private bool _devicesTouched;
    private GoalStepsFrom? _stepsFrom;
    private readonly string? _mainPhoneName;
    private readonly IReadOnlyList<GoalAppMatch> _fromGoals;
    private readonly IReadOnlyList<TaskLabel>? _pickerLabels;
    private readonly IReadOnlyList<TaskLabelGroup> _pickerGroups;

    public GoalSourceRowViewModel(GoalSourceRule rule, GoalEditorLookups lookups, bool locked, Action<GoalSourceRowViewModel> remove, Action changed)
    {
        _rule = rule;
        _remove = remove;
        _changed = changed;
        _beeminderConnected = lookups.BeeminderConnected;
        _groupHasPhone = lookups.GroupHasPhone;
        _inSyncGroup = lookups.InSyncGroup;
        _hasAndroidDevice = lookups.HasAndroidDevice;
        _stepsFrom = rule.StepsFrom;
        _mainPhoneName = lookups.MainPhoneName;
        _fromGoals = lookups.FromGoals ?? [];
        IsLocked = locked;
        Labels = lookups.Labels;
        _pickerLabels = lookups.PickerLabels;
        _pickerGroups = lookups.PickerGroups ?? [];
        // A label another device keeps to itself (ADR 89) still counts: the row keeps it, says where it is from and never shows its name.
        if (rule.LabelId is Guid kept && !Labels.Any(choice => choice.Key == kept.ToString()) && lookups.HiddenLabelNote?.Invoke(kept) is string hiddenNote)
        {
            Labels = [.. Labels, new GoalChoice(kept.ToString(), hiddenNote, Icon: MaterialIconKind.Laptop)];
        }

        Categories = lookups.CalendarCategories;
        BeeminderGoals = lookups.BeeminderGoals;
        BeeminderReadText = lookups.BeeminderReadText;
        BeeminderProblemText = lookups.BeeminderProblemText;
        Automatic = rule.Automatic;
        SelectedLabel = Labels.FirstOrDefault(choice => rule.LabelId is Guid id && choice.Key == id.ToString());
        Tag = rule.Tag ?? string.Empty;
        // A tag another device keeps to itself (ADR 89) still counts: the row keeps it (the saved rule), says where it is from and never shows its name.
        HiddenTagNote = rule.Type == GoalSourceType.FocusTag && rule.Tag is { } hiddenTag ? lookups.HiddenTagNote?.Invoke(hiddenTag) : null;
        SelectedCategory = Categories.FirstOrDefault(choice => rule.CalendarCategoryId is Guid id && choice.Key == id.ToString());
        _selfDeviceId = lookups.SelfDeviceId;
        if (rule.Type == GoalSourceType.ScreenTime)
        {
            // The pick list's 30 days are read here, when a screen-time row opens, and not when the dialog does.
            _seen = lookups.Seen ?? lookups.ReadSeen?.Invoke() ?? GoalScreenTimeSeen.Empty;
        }

        AddText = string.Empty;
        if (rule.Type == GoalSourceType.ScreenTime)
        {
            foreach (GoalAppMatch match in rule.Apps ?? [])
            {
                Entries.Add(NewEntry(match, match));
            }

            BuildDevices(rule, lookups);
            foreach (GoalAppEntryViewModel entry in Entries)
            {
                entry.RefreshDevices(notify: false); // the devices exist now (the entries were made first)
            }

            RefreshPicks();
            RebuildSections();
        }

        ScreenAtMost = rule.ScreenDirection != GoalDirection.AtLeast;
        // By key (the slug) first; by title only when exactly one Beeminder goal has it. A title that fits several goals is not guessed
        // (the datapoint read skips that link too): the row says to pick the goal again.
        List<GoalChoice> byTitle = rule.BeeminderSlug is null ? [] : BeeminderGoals.Where(choice => choice.Text == rule.BeeminderSlug).ToList();
        GoalChoice? byKey = BeeminderGoals.FirstOrDefault(choice => choice.Key == rule.BeeminderSlug);
        bool ambiguous = byKey is null && byTitle.Count > 1;
        SelectedBeeminder = byKey
            ?? (byTitle.Count == 1 ? byTitle[0] : null)
            ?? (rule.BeeminderSlug is string slug ? new GoalChoice(slug, slug, Note: ambiguous ? PickAgainText : "Not in the cached list") : null);
        if (ambiguous)
        {
            BeeminderProblemText = string.IsNullOrEmpty(BeeminderProblemText) ? PickAgainText : PickAgainText + "\n" + BeeminderProblemText;
        }

        if (SelectedBeeminder is not null && !BeeminderGoals.Contains(SelectedBeeminder))
        {
            BeeminderGoals = [.. BeeminderGoals, SelectedBeeminder];
        }
    }

    public GoalSourceType Type => _rule.Type;

    public Guid Id => _rule.Id;

    /// <summary>Deep work's "all counted focus": no switch, no remove, no other source (plan §3b 6).</summary>
    public bool IsLocked { get; }

    public bool IsEditable => !IsLocked;

    public IReadOnlyList<GoalChoice> Labels { get; }

    public IReadOnlyList<GoalChoice> Categories { get; }

    public IReadOnlyList<GoalChoice> BeeminderGoals { get; }

    /// <summary>"Read 2:05 PM" (ADR 84): the last read that worked, also while a later one failed; null before the first.</summary>
    public string? BeeminderReadText { get; }

    /// <summary>"Couldn't reach Beeminder · keeps the last values" after a failed read, and "Pick the Beeminder goal again" when this link's title fits several goals; null otherwise.</summary>
    public string? BeeminderProblemText { get; }

    private const string PickAgainText = "Pick the Beeminder goal again";

    public string Title => _rule.Type switch
    {
        GoalSourceType.FocusAll => "All counted focus",
        GoalSourceType.FocusLabel => "Focus time on a label",
        GoalSourceType.FocusTag => "Focus time on a #tag",
        GoalSourceType.Hand => "Tick by hand",
        GoalSourceType.HealthSteps => _stepsFrom switch
        {
            GoalStepsFrom.Watch => "Pixel Watch · steps (your phone's on days without watch steps)",
            GoalStepsFrom.Phone => "This phone · steps",
            _ => "Health Connect · steps",
        },
        GoalSourceType.HealthSleep => "Health Connect · sleep",
        GoalSourceType.ScreenTime => "Screen time",
        GoalSourceType.CalendarCategory => "Google Calendar",
        GoalSourceType.GoalField => "A task carries this goal",
        GoalSourceType.MilestoneTasks => "Milestone tasks",
        GoalSourceType.Beeminder => SelectedBeeminder is GoalChoice linked ? $"Beeminder · {linked.Key}" : "Beeminder",
        _ => _rule.Type.ToString(),
    };

    public string Detail => _rule.Type switch
    {
        // NG1: a source already chosen stays; with no phone in the group it says nothing arrives for it now.
        // SR1-P2 (plan check S7): Android devices are in the group but none counts (no main phone, none without a role): say what to do.
        GoalSourceType.HealthSteps or GoalSourceType.HealthSleep when !_groupHasPhone && _hasAndroidDevice
            => DetailOfType + (_rule.Type == GoalSourceType.HealthSteps ? " Set a main phone to count steps." : " Set a main phone to count sleep."),
        GoalSourceType.HealthSteps or GoalSourceType.HealthSleep when !_groupHasPhone
            => DetailOfType + (_inSyncGroup ? " No phone is in your group now, so nothing arrives until one joins." : " Not in a sync group, so nothing arrives from a phone."),
        _ => DetailOfType,
    };

    private string DetailOfType => _rule.Type switch
    {
        GoalSourceType.FocusAll => "Every counted minute of focus, as the guards count it. Deep work counts nothing else.",
        GoalSourceType.FocusLabel => "Sessions with a task carrying this label.",
        GoalSourceType.FocusTag => "Sessions with a task carrying this #tag. Counted on this PC (the phone shows it as measured on your PC).",
        GoalSourceType.Hand => "Here or on Today.",
        GoalSourceType.HealthSteps => StepsDetail,
        GoalSourceType.HealthSleep => "Measured on your phone (Health Connect). Its daily values count here once your phone syncs them.",
        GoalSourceType.ScreenTime => "Time in these apps and sites. This PC counts them from its own activity history; the devices you tick add theirs.",
        GoalSourceType.CalendarCategory => "Time in events of this day plan category. This PC only; it never counts as focus or for a guard.",
        GoalSourceType.GoalField => "Focus in sessions whose checklist holds a task linked to this goal (task menu › Link to goal). Counted on every device from its task list.",
        GoalSourceType.MilestoneTasks => "Each task linked to this goal that you finish counts one, on the day you finished it. Counted on every device from its task list.",
        GoalSourceType.Beeminder when !_beeminderConnected => "Read on the PC that has your Beeminder token; its daily values reach this PC when it syncs",
        GoalSourceType.Beeminder => SelectedBeeminder?.Note is string note ? $"Counts its daily values on this PC · {note}" : "Counts its daily values on this PC",
        _ => string.Empty,
    };

    // ADR 91: what each choice counts; the phone that reads is the main phone when one is set (ADR 90).
    private string StepsDetail
    {
        get
        {
            string where = _mainPhoneName is { } name ? $" Counted on {name}, your main phone." : string.Empty;
            return _stepsFrom switch
            {
                GoalStepsFrom.Watch => "Steps your watch recorded; on a day it recorded none, your phone's, and the day says which." + where,
                GoalStepsFrom.Phone => "Only the steps your phone itself recorded." + where,
                _ => "Measured on your phone (Health Connect, Pixel Watch through it). Daily steps already reach this PC." + where,
            };
        }
    }

    public bool ShowsSteps => _rule.Type == GoalSourceType.HealthSteps;

    /// <summary>Which steps count (null = all); the steps tiles read it and <see cref="SetStepsFrom"/> changes it.</summary>
    public GoalStepsFrom? StepsFrom => _stepsFrom;

    /// <summary>The "All" end of the segmented control (B10 of the SR1 plan check): all of Health Connect's steps.</summary>
    public bool StepsAll
    {
        get => _stepsFrom is null;
        set
        {
            if (value)
            {
                SetStepsFrom(null);
            }
        }
    }

    public bool StepsWatch
    {
        get => _stepsFrom == GoalStepsFrom.Watch;
        set
        {
            if (value)
            {
                SetStepsFrom(GoalStepsFrom.Watch);
            }
        }
    }

    public bool StepsPhone
    {
        get => _stepsFrom == GoalStepsFrom.Phone;
        set
        {
            if (value)
            {
                SetStepsFrom(GoalStepsFrom.Phone);
            }
        }
    }

    /// <summary>Changes which steps count (a tile or the segmented control); the goal's Sources part is stamped once when the goal is saved.</summary>
    public void SetStepsFrom(GoalStepsFrom? stepsFrom)
    {
        if (IsLocked || !ShowsSteps || _stepsFrom == stepsFrom)
        {
            return;
        }

        _stepsFrom = stepsFrom;
        OnPropertyChanged(nameof(StepsAll));
        OnPropertyChanged(nameof(StepsWatch));
        OnPropertyChanged(nameof(StepsPhone));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        _changed();
    }

    public MaterialIconKind Icon => GoalSourceIcons.Of(_rule.Type);

    public bool ShowsLabel => _rule.Type == GoalSourceType.FocusLabel;

    public bool ShowsTag => _rule.Type == GoalSourceType.FocusTag;

    /// <summary>"Uses a label from Laptop" when the source's tag is another device's device-only one (ADR 89); null otherwise.</summary>
    public string? HiddenTagNote { get; }

    /// <summary>The tag box shows unless the tag is hidden here (then only the note shows, and the rule keeps the tag).</summary>
    public bool ShowsTagBox => ShowsTag && HiddenTagNote is null;

    public bool ShowsHiddenTagNote => ShowsTag && HiddenTagNote is not null;

    public bool ShowsCategory => _rule.Type == GoalSourceType.CalendarCategory;

    public bool ShowsApp => _rule.Type == GoalSourceType.ScreenTime;

    public bool ShowsBeeminder => _rule.Type == GoalSourceType.Beeminder;

    public bool ShowsSwitch => !IsLocked && !ShowsBeeminder;

    public bool HasNoLabels => ShowsLabel && Labels.Count == 0;

    /// <summary>Tasks redesign 10.32: the label is picked with the grouped label picker (a button) rather than the plain list.</summary>
    public bool ShowsLabelPicker => ShowsLabel && _pickerLabels is not null && Labels.Count > 0;

    public bool ShowsLabelList => ShowsLabel && !ShowsLabelPicker;

    /// <summary>The picked label's dot on the picker button (none before a pick).</summary>
    public Avalonia.Media.IBrush? LabelPickBrush => SelectedLabel?.Brush;

    /// <summary>The picker button's text: the label picked, or "Pick a label".</summary>
    public string LabelPickText => SelectedLabel?.Text ?? "Pick a label";

    /// <summary>
    /// The grouped label picker (categories with icons, labels under them; ADR 93), with the label picked now tagged "Current". A goal counts
    /// one label, never a whole category (owner decision), and "No label" is not offered. Null when the plain list shows.
    /// </summary>
    public LabelPickerViewModel? LabelPicker()
    {
        if (_pickerLabels is null)
        {
            return null;
        }

        Guid? current = SelectedLabel is GoalChoice picked && Guid.TryParse(picked.Key, out Guid id) ? id : null;
        return new LabelPickerViewModel(_pickerLabels, _pickerGroups, current is Guid held ? [held] : []);
    }

    /// <summary>A pick from <see cref="LabelPicker"/>: that label (a "No label" pick changes nothing).</summary>
    public void ApplyLabelPick(LabelPick pick)
    {
        if (pick.LabelId is Guid id && Labels.FirstOrDefault(choice => choice.Key == id.ToString()) is GoalChoice choice)
        {
            SelectedLabel = choice;
        }
    }

    public bool HasNoCategories => ShowsCategory && Categories.Count == 0;

    /// <summary>Counts by itself (on) or listed only (off).</summary>
    [ObservableProperty]
    public partial bool Automatic { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedLabel { get; set; }

    [ObservableProperty]
    public partial string Tag { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedCategory { get; set; }

    /// <summary>The apps and sites that count (ST2): a row each, with its kind chip.</summary>
    public ObservableCollection<GoalAppEntryViewModel> Entries { get; } = [];

    /// <summary>What this PC saw in the last 30 days and is not in the list yet (narrowed by what is typed): a tap adds it.</summary>
    public ObservableCollection<GoalSeenPickViewModel> Picks { get; } = [];

    /// <summary>The devices whose minutes count: this PC first, then the group's others.</summary>
    public ObservableCollection<GoalDeviceOptionViewModel> Devices { get; } = [];

    /// <summary>ST4 "From your goals": entries already in goals (not listed here yet), narrowed by the typed text: a tap adds one.</summary>
    public ObservableCollection<GoalSeenPickViewModel> GoalPicks { get; } = [];

    public bool HasNoGoalPicks => GoalPicks.Count == 0;

    /// <summary>ST4: a line for each ticked device with the entries that count there (shown with two or more devices ticked).</summary>
    public ObservableCollection<GoalDeviceSectionViewModel> Sections { get; } = [];

    public bool ShowsSections => Sections.Count >= 2;

    public bool HasNoPicks => Picks.Count == 0;

    /// <summary>Why the pick list is empty.</summary>
    public string NoPicksText => _seen.Items.Count == 0 ? "This PC has not seen any app or site yet." : "Nothing else matches.";

    /// <summary>The text of the Add box; the kind is guessed as it is typed (<see cref="GoalAppMatchGuess"/>) and can be changed on the entry's chip.</summary>
    [ObservableProperty]
    public partial string AddText { get; set; }

    /// <summary>"Adds as Website": what the box's text becomes; empty while the box is.</summary>
    public string GuessText => GoalAppMatchGuess.Guess(AddText) is { } guess ? "Adds as " + GoalAppKindChoice.Of(guess.Kind).Text : string.Empty;

    [ObservableProperty]
    public partial bool ScreenAtMost { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedBeeminder { get; set; }

    /// <summary>What still needs a choice before the source counts ("Pick a label"); null when complete.</summary>
    public string? Missing => _rule.Type switch
    {
        GoalSourceType.FocusLabel when SelectedLabel is null => "Pick a label for the focus source.",
        GoalSourceType.FocusTag when string.IsNullOrWhiteSpace(Tag.TrimStart('#')) => "Type the #tag for the focus source.",
        GoalSourceType.CalendarCategory when SelectedCategory is null => "Pick the day plan category for the calendar source.",
        GoalSourceType.ScreenTime when !string.IsNullOrWhiteSpace(AddText) => "Press Add to keep the text in the box, or clear it.",
        GoalSourceType.ScreenTime when Entries.Count == 0 => "Add at least one app or site for screen time.",
        GoalSourceType.ScreenTime when _devicesTouched && !Devices.Any(device => device.IsChecked && device.IsEnabled) => "Tick at least one device to count from.",
        GoalSourceType.Beeminder when SelectedBeeminder is null => "Pick a Beeminder goal to read.",
        _ => null,
    };

    /// <summary>The rule as it is set now; <paramref name="threshold"/> = a habit's "ticks itself at" for time sources.</summary>
    public GoalSourceRule ToRule(double? threshold)
    {
        if (IsLocked)
        {
            return _rule;
        }

        return _rule with
        {
            Automatic = ShowsBeeminder || Automatic,
            LabelId = ShowsLabel && SelectedLabel is GoalChoice label && Guid.TryParse(label.Key, out Guid labelId) ? labelId : null,
            Tag = ShowsTag ? Tag.Trim().TrimStart('#') : null,
            CalendarCategoryId = ShowsCategory && SelectedCategory is GoalChoice category && Guid.TryParse(category.Key, out Guid categoryId) ? categoryId : null,
            // B7 of the plan check: one entry per kind and text; a duplicate's device list joins the first.
            Apps = ShowsApp ? GoalAppMatching.OneEntryPerKindAndText(Entries.Select(entry => entry.ToMatch()).ToList()) : _rule.Apps,
            StepsFrom = ShowsSteps ? _stepsFrom : _rule.StepsFrom,
            ScreenDirection = ShowsApp ? (ScreenAtMost ? GoalDirection.AtMost : GoalDirection.AtLeast) : null,
            BeeminderSlug = ShowsBeeminder ? SelectedBeeminder?.Key : null,
            BeeminderDirection = ShowsBeeminder ? GoalBeeminderDirection.Read : null,
            ThresholdMinutes = _rule.CountsTime ? threshold : null,
            Devices = DevicesChosen ? DevicesToStore() : _rule.Devices,
            AllDevices = !DevicesChosen && _rule.AllDevices,
            CreatedOn = _rule.CreatedOn ?? (DevicesChosen && _selfDeviceId != Guid.Empty ? _selfDeviceId : null),
        };
    }

    // The devices were changed here (an untouched list leaves the rule's devices as they were, also for a rule written before devices were recorded).
    private bool DevicesChosen => ShowsApp && _devicesTouched;

    // Only the creating device ticked = no list (the rule's default); else the ticked ids.
    private IReadOnlyList<Guid>? DevicesToStore()
    {
        IReadOnlyList<Guid> picked = PickedDevices();
        Guid? created = _rule.CreatedOn ?? (_selfDeviceId == Guid.Empty ? null : _selfDeviceId);
        return picked.Count == 1 && picked[0] == created ? null : picked;
    }

    // The ticked devices, plus the ids of devices that left the group and are no longer listed (their past days still count).
    private IReadOnlyList<Guid> PickedDevices()
    {
        List<Guid> listed = Devices.Select(device => device.Id).ToList();
        IEnumerable<Guid> kept = (_rule.Devices ?? (_rule.CreatedOn is Guid created ? [created] : [])).Where(id => !listed.Contains(id));
        return Devices.Where(device => device.IsChecked).Select(device => device.Id).Concat(kept).Distinct().ToList();
    }

    // The same thing: the same kind and text, ignoring case, a program's ".exe" and a site's "www." (the way they match).
    private static bool Same(GoalAppMatch a, GoalAppMatch b)
    {
        return a.Kind == b.Kind && string.Equals(Plain(a), Plain(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Plain(GoalAppMatch match)
    {
        string text = match.Text.Trim();
        return match.Kind switch
        {
            GoalAppMatchKind.Process when text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) => text[..^4],
            GoalAppMatchKind.Website when text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) => text[4..],
            _ => text,
        };
    }

    private GoalAppEntryViewModel NewEntry(GoalAppMatch match, GoalAppMatch? original)
    {
        return new GoalAppEntryViewModel(match, original, _seen.WasSeen, entry =>
        {
            Entries.Remove(entry);
            EntriesChanged();
        }, EntriesChanged, TickedDevices);
    }

    // The devices the source counts from now, for each entry's chip.
    private IReadOnlyList<GoalDeviceOptionViewModel> TickedDevices() => Devices.Where(device => device.IsChecked).ToList();

    private void EntriesChanged()
    {
        RefreshPicks();
        RebuildSections();
        Edited();
    }

    // One line per ticked device: the entries that count there (their own list, and the kind: programs and sites on a PC, phone apps elsewhere).
    private void RebuildSections()
    {
        Sections.Clear();
        List<GoalDeviceOptionViewModel> ticked = TickedDevices().ToList();
        if (ticked.Count >= 2)
        {
            foreach (GoalDeviceOptionViewModel device in ticked)
            {
                List<string> counted = Entries
                    .Where(entry => entry.CountsOn(device.Id) && (entry.SelectedKind.Kind == GoalAppMatchKind.Name || (entry.SelectedKind.Kind == GoalAppMatchKind.Package) != device.IsPc))
                    .Select(entry => entry.Text)
                    .ToList();
                Sections.Add(new GoalDeviceSectionViewModel(
                    device.Name.EndsWith(" (this PC)", StringComparison.Ordinal) ? device.Name[..^10] : device.Name, device.Icon,
                    counted.Count == 0 ? "Nothing counts here" : string.Join(", ", counted), counted.Count == 0));
            }
        }

        OnPropertyChanged(nameof(ShowsSections));
    }

    /// <summary>Adds the Add box's text as an entry of its guessed kind (adding the same thing twice is ignored); nothing when the box is empty.</summary>
    [RelayCommand]
    private void AddEntry()
    {
        if (GoalAppMatchGuess.Guess(AddText) is not { } guess)
        {
            return;
        }

        AddMatch(guess);
        AddText = string.Empty;
    }

    private void AddMatch(GoalAppMatch match)
    {
        if (!Entries.Any(entry => Same(entry.ToMatch(), match)))
        {
            Entries.Add(NewEntry(match, null));
        }

        EntriesChanged();
    }

    // The pick list: this PC's seen things not in the list yet, narrowed by the typed text (shown names and real names, any case).
    private void RefreshPicks()
    {
        string typed = AddText?.Trim() ?? string.Empty;
        Picks.Clear();
        foreach (GoalScreenTimeSeenItem item in _seen.Items)
        {
            bool listed = Entries.Any(entry => Same(entry.ToMatch(), item.Match));
            bool fits = typed.Length == 0
                || item.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || item.Match.Text.Contains(typed, StringComparison.OrdinalIgnoreCase);
            if (!listed && fits)
            {
                Picks.Add(new GoalSeenPickViewModel(item, picked =>
                {
                    AddMatch(picked.Match);
                    AddText = string.Empty; // the box was only a filter for the list
                }));
            }
        }

        OnPropertyChanged(nameof(HasNoPicks));
        OnPropertyChanged(nameof(NoPicksText));

        // "From your goals": the goals' entries that this PC's list doesn't already offer or this source doesn't already hold.
        GoalPicks.Clear();
        foreach (GoalAppMatch match in _fromGoals)
        {
            bool listed = Entries.Any(entry => Same(entry.ToMatch(), match)) || _seen.Items.Any(item => Same(item.Match, match));
            if (!listed && (typed.Length == 0 || match.Text.Contains(typed, StringComparison.OrdinalIgnoreCase)))
            {
                GoalScreenTimeSeenItem item = new(match, match.Text, match.Kind == GoalAppMatchKind.Website, 0);
                GoalPicks.Add(new GoalSeenPickViewModel(item, picked =>
                {
                    AddMatch(picked.Match);
                    AddText = string.Empty;
                }));
            }
        }

        OnPropertyChanged(nameof(HasNoGoalPicks));
    }

    // This PC first (ticked when the rule counts it), then the group's other devices. Other devices need goals sync (ADR 79); without it they are greyed.
    private void BuildDevices(GoalSourceRule rule, GoalEditorLookups lookups)
    {
        Devices.Add(new GoalDeviceOptionViewModel(
            lookups.SelfDeviceId, lookups.SelfDeviceName + " (this PC)", MaterialIconKind.Laptop, GoalAppMatching.CountsOn(rule, lookups.SelfDeviceId), true, null, DevicesChanged, isPc: true));
        foreach (GoalChoice other in lookups.OtherDevices ?? [])
        {
            if (!Guid.TryParse(other.Key, out Guid id))
            {
                continue;
            }

            string? note = lookups.GoalsSyncOn ? null : "Needs goals sync: turn on Sync task lists";
            Devices.Add(new GoalDeviceOptionViewModel(
                id, other.Text, other.Icon ?? MaterialIconKind.Devices, GoalAppMatching.CountsOn(rule, id), lookups.GoalsSyncOn, note, DevicesChanged, isPc: other.IsPc));
        }
    }

    private void DevicesChanged()
    {
        _devicesTouched = true;
        foreach (GoalAppEntryViewModel entry in Entries)
        {
            entry.RefreshDevices();
        }

        RebuildSections();
        Edited();
    }

    [RelayCommand]
    private void Remove()
    {
        if (!IsLocked)
        {
            _remove(this);
        }
    }

    partial void OnAutomaticChanged(bool value) => _changed();

    partial void OnSelectedLabelChanged(GoalChoice? value)
    {
        OnPropertyChanged(nameof(LabelPickText));
        OnPropertyChanged(nameof(LabelPickBrush));
        Edited();
    }

    partial void OnTagChanged(string value) => Edited();

    partial void OnSelectedCategoryChanged(GoalChoice? value) => Edited();

    partial void OnAddTextChanged(string value)
    {
        RefreshPicks();
        OnPropertyChanged(nameof(GuessText));
        Edited();
    }

    partial void OnScreenAtMostChanged(bool value) => _changed();

    partial void OnSelectedBeeminderChanged(GoalChoice? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        Edited();
    }

    private void Edited()
    {
        OnPropertyChanged(nameof(Missing));
        _changed();
    }
}
