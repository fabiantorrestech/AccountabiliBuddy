using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Goals;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Edit goal › Tracking &amp; sources (round 2 R5.7, brief §4.4, §6.1, §6.2; board Goals-PC-Edit): "Counting now" (each source with its
/// setting and switch), "Add a source" (the available tiles, then the greyed "Coming soon…" ones), and the rule line "Sources add up ·
/// overlapping time counts once · your edits win". Deep work's sources stay locked to all counted focus (plan §3b 6), and it says why.
/// Only sources <see cref="GoalSources.AllowedFor"/> allows can be added; nothing here connects to anything (Beeminder reads its cached
/// list only, ADR 43).
/// </summary>
public sealed partial class GoalSourcesEditorViewModel : ObservableObject
{
    private GoalEditorLookups _lookups = GoalEditorLookups.Empty;
    private bool _deepWork;

    public GoalSourcesEditorViewModel()
    {
    }

    /// <summary>Raised on every change (the nav line, the Problem text).</summary>
    public event EventHandler? Changed;

    public ObservableCollection<GoalSourceRowViewModel> Rows { get; } = [];

    public ObservableCollection<GoalSourceTileViewModel> Tiles { get; } = [];

    public ObservableCollection<GoalSourceTileViewModel> SoonTiles { get; } = [];

    public string RuleLine => "Sources add up · overlapping time counts once · your edits win";

    [ObservableProperty]
    public partial string CountText { get; set; } = "COUNTING NOW · 0";

    public bool IsDeepWork => _deepWork;

    public string LockedReason => "Deep work counts all your counted focus and nothing else: it is what the guards and the goal unlock read, so no hand, calendar or phone source can be added.";

    /// <summary>The goal has no source yet (it counts nothing until one is added).</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Shows a goal's sources (a new goal: Tick by hand to start with).</summary>
    public void Load(Goal goal, GoalEditorLookups lookups)
    {
        _lookups = lookups;
        _deepWork = goal.IsDeepWork;
        Rows.Clear();
        IEnumerable<GoalSourceRule> rules = goal.IsDeepWork ? GoalSources.CountedFor(goal) : goal.Sources;
        foreach (GoalSourceRule rule in rules)
        {
            Rows.Add(NewRow(rule));
        }

        BuildTiles();
        Refresh();
        OnPropertyChanged(nameof(IsDeepWork));
    }

    /// <summary>The rules to save (the habit's threshold on its time sources); Deep work's are its own.</summary>
    public IReadOnlyList<GoalSourceRule> Build(double? threshold)
    {
        return Rows.Select(row => row.ToRule(threshold)).ToList();
    }

    /// <summary>The first source still missing a choice ("Pick a label…"), or null.</summary>
    public string? Problem => Rows.Select(row => row.Missing).FirstOrDefault(missing => missing is not null);

    /// <summary>"3 sources", "Tick by hand", "All counted focus": the nav line.</summary>
    public string Summary => Rows.Count switch
    {
        0 => "No source yet",
        1 => Rows[0].Title,
        _ => $"{Rows.Count} sources",
    };

    /// <summary>Adds a source of <paramref name="type"/> (as its tile does); false when this goal can't take it.</summary>
    public bool Add(GoalSourceType type)
    {
        if (_deepWork || type == GoalSourceType.FocusAll || GoalSources.AvailabilityOf(type) == GoalSourceAvailability.ComingSoon)
        {
            return false;
        }

        if (IsSingle(type) && Rows.Any(row => row.Type == type))
        {
            return false;
        }

        Rows.Add(NewRow(new GoalSourceRule(Guid.NewGuid(), type)));
        Refresh();
        return true;
    }

    private GoalSourceRowViewModel NewRow(GoalSourceRule rule)
    {
        return new GoalSourceRowViewModel(rule, _lookups, _deepWork, Remove, () => Refresh());
    }

    private void Remove(GoalSourceRowViewModel row)
    {
        Rows.Remove(row);
        Refresh();
    }

    private void Refresh()
    {
        CountText = $"COUNTING NOW · {Rows.Count}";
        foreach (GoalSourceTileViewModel tile in Tiles)
        {
            tile.IsLocked = _deepWork;
            tile.IsAdded = tile.Type is GoalSourceType type && IsSingle(type) && Rows.Any(row => row.Type == type);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Sources that make sense once per goal; labels, tags, categories and apps can be added several times.
    private static bool IsSingle(GoalSourceType type) => type is GoalSourceType.Hand or GoalSourceType.HealthSteps or GoalSourceType.HealthSleep;

    private void BuildTiles()
    {
        Tiles.Clear();
        SoonTiles.Clear();
        void Available(string key, string title, string text, MaterialIconKind icon, GoalSourceType type, string? note = null)
        {
            Tiles.Add(new GoalSourceTileViewModel(key, title, text, icon, type, tile => Add(type), note));
        }

        Available("focusLabel", "Focus time · label", "Focus in AccountabiliBuddy on tasks with a label", MaterialIconKind.TimerOutline, GoalSourceType.FocusLabel);
        Available("focusTag", "Focus time · #tag", "Focus on tasks with a #tag", MaterialIconKind.Pound, GoalSourceType.FocusTag, "Counted on this PC");
        Available("hand", "Tick by hand", "Tick it, log a value or add time here or on Today", MaterialIconKind.HandBackRightOutline, GoalSourceType.Hand);
        Available("calendar", "Google Calendar", "Time in events of a day plan category", MaterialIconKind.CalendarOutline, GoalSourceType.CalendarCategory, "This PC only · never focus");
        Available("healthConnect", "Health Connect", "Steps from your phone and apps that write to it", MaterialIconKind.HeartPulse, GoalSourceType.HealthSteps, "Measured on your phone");
        Available("pixelWatch", "Pixel Watch", "Steps from the watch, through Health Connect", MaterialIconKind.WatchVariant, GoalSourceType.HealthSteps, "Measured on your phone");
        Available("screenTime", "Screen time", "Time in an app or category on your phone", MaterialIconKind.Monitor, GoalSourceType.ScreenTime, "Measured on your phone");
        if (_lookups.BeeminderConnected)
        {
            Available("beeminder", "Beeminder", "Read one of your goals from the list this PC keeps", MaterialIconKind.Bee, GoalSourceType.Beeminder, "Read · Send coming soon");
        }

        void Soon(string key, string title, string text, MaterialIconKind icon, GoalSourceType? type = null)
        {
            SoonTiles.Add(new GoalSourceTileViewModel(key, title, text, icon, type, add: null));
        }

        Soon("goalField", "A task carries this goal", "Tasks with this goal count their focus", MaterialIconKind.LinkVariant, GoalSourceType.GoalField);
        Soon("milestoneTasks", "Milestone tasks", "The milestone's own task list", MaterialIconKind.FormatListChecks, GoalSourceType.MilestoneTasks);
        Soon("garmin", "Garmin", "Activities and steps", MaterialIconKind.Watch);
        Soon("focusmate", "Focusmate", "Sessions you booked", MaterialIconKind.AccountGroupOutline);
        Soon("toggl", "Toggl Track", "Tracked time by project", MaterialIconKind.TimerPlayOutline);
        Soon("rescueTime", "RescueTime", "Time by category", MaterialIconKind.ClockOutline);
        Soon("github", "GitHub", "Commits and pull requests", MaterialIconKind.SourceBranch);
        Soon("strava", "Strava", "Runs and rides", MaterialIconKind.Bike);
        Soon("webhook", "Webhook", "IFTTT, Zapier, Home Assistant, scripts", MaterialIconKind.Webhook);
        Soon("csv", "CSV import", "Values from a file", MaterialIconKind.FileDelimitedOutline);
    }
}
