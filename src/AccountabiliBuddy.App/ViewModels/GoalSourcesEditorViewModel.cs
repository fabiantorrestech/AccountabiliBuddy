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
    private GoalKind _kind = GoalKind.Habit;

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
        _kind = goal.Kind;
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

    /// <summary>The goal's kind changed in the editor (GS5: milestone tasks count only on a milestone).</summary>
    public void SetKind(GoalKind kind)
    {
        _kind = kind;
        Refresh();
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

        if (type == GoalSourceType.MilestoneTasks && _kind != GoalKind.Milestone)
        {
            return false;
        }

        if (IsSingle(type) && Rows.Any(row => row.Type == type))
        {
            return false;
        }

        // A screen-time source remembers the device that made it (the default of its device list, ST2).
        Guid? self = _lookups.SelfDeviceId == Guid.Empty ? null : _lookups.SelfDeviceId;
        Rows.Add(NewRow(new GoalSourceRule(Guid.NewGuid(), type) { CreatedOn = type == GoalSourceType.ScreenTime ? self : null }));
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
            tile.Unavailable = tile.Type == GoalSourceType.MilestoneTasks && _kind != GoalKind.Milestone ? "Milestone tasks only count on a milestone goal" : null;
            tile.IsAdded = tile.Type is GoalSourceType type && IsSingle(type) && Rows.Any(row => row.Type == type);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(Summary));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Sources that make sense once per goal; labels, tags and categories can be added several times. Screen time holds a list of apps and sites itself.
    private static bool IsSingle(GoalSourceType type) => type is GoalSourceType.Hand or GoalSourceType.HealthSteps or GoalSourceType.HealthSleep or GoalSourceType.ScreenTime or GoalSourceType.GoalField or GoalSourceType.MilestoneTasks;

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
        Available("goalField", "A task carries this goal", "Focus in sessions that hold a task linked to this goal", MaterialIconKind.LinkVariant, GoalSourceType.GoalField, "Link tasks from their menu");
        Available("milestoneTasks", "Milestone tasks", "Each task linked to this goal and finished counts one", MaterialIconKind.FormatListChecks, GoalSourceType.MilestoneTasks, "For milestones · this PC");
        Available("hand", "Tick by hand", "Tick it, log a value or add time here or on Today", MaterialIconKind.HandBackRightOutline, GoalSourceType.Hand);
        Available("calendar", "Google Calendar", "Time in events of a day plan category", MaterialIconKind.CalendarOutline, GoalSourceType.CalendarCategory, "This PC only · never focus");
        // NG1: sources measured only on a phone are offered when a phone is in the group. Screen time (ST2) is measured on this PC too, so it is always offered.
        if (_lookups.GroupHasPhone)
        {
            Available("healthConnect", "Health Connect", "Steps from your phone and apps that write to it", MaterialIconKind.HeartPulse, GoalSourceType.HealthSteps, "Measured on your phone");
            Available("pixelWatch", "Pixel Watch", "Steps from the watch, through Health Connect", MaterialIconKind.WatchVariant, GoalSourceType.HealthSteps, "Measured on your phone");
        }

        Available("screenTime", "Screen time", "Time in apps and sites on this PC, and on the devices you pick", MaterialIconKind.Monitor, GoalSourceType.ScreenTime, "This PC and your other devices");

        if (_lookups.BeeminderConnected)
        {
            Available("beeminder", "Beeminder", "Counts one of your Beeminder goals day by day on this PC", MaterialIconKind.Bee, GoalSourceType.Beeminder, "Read · Send coming soon");
        }

        void Soon(string key, string title, string text, MaterialIconKind icon, GoalSourceType? type = null)
        {
            SoonTiles.Add(new GoalSourceTileViewModel(key, title, text, icon, type, add: null));
        }

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
