using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.App.Services.Setup;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Import;
using AccountabiliBuddy.Core.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant. The first run asks three questions (<see cref="BeginQuick"/>: what kind of computer → link to your
/// other devices → a daily goal → done); the full setup (More → Setup) is: welcome → link to your other devices → what kind of computer → bring your blocks (other blockers found on this PC, a LeechBlock file, or a pasted
/// list) → your usage (suggestions from Cold Turkey's statistics, optional copy into History) → review (profiles and
/// their rules) → browsers → basics → done. The import is saved when leaving the review page, in one
/// change through <see cref="ConfigurationService"/>; it only adds profiles. Importing is locked during a
/// focus session.
/// </summary>
public sealed partial class SetupAssistantViewModel : ObservableObject, IDisposable
{
    public const string NewProfileTarget = "A new profile: Top distractions";

    private const int SuggestionCount = 12;
    private static readonly TimeSpan SuggestionPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan HistoryPeriod = TimeSpan.FromDays(90);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

    private readonly IBlockerSourceLocator _locator;
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;
    private readonly IActivityLog _activityLog;
    private readonly PrivacySettingsService _privacy;
    private readonly TimeProvider _time;
    private ColdTurkeyDataFiles? _coldTurkeyFiles;
    private ImportPlan _plan = ImportPlan.Empty;
    private bool _hasScanned;
    private bool _hasImported;

    public SetupAssistantViewModel(
        IBlockerSourceLocator locator,
        ConfigurationService configuration,
        SessionController sessions,
        IActivityLog activityLog,
        PrivacySettingsService privacy,
        TimeProvider time,
        SetupViewModel setup,
        BackupViewModel backup)
    {
        _locator = locator;
        _configuration = configuration;
        _sessions = sessions;
        _activityLog = activityLog;
        _privacy = privacy;
        _time = time;
        Setup = setup;
        Backup = backup;
        PasteText = string.Empty;
        PasteName = "Pasted list";
        WeekPreview = string.Empty;
        Summary = string.Empty;
        IsLocked = sessions.IsEnforcing;
        _sessions.StateChanged += OnSessionStateChanged;
    }

    /// <summary>Shared with the Setup tab: browsers, start with Windows, quick session, data permissions.</summary>
    public SetupViewModel Setup { get; }

    /// <summary>Restoring a backup made on another PC (import only).</summary>
    public BackupViewModel Backup { get; }

    /// <summary>Set by the window: shows an open-file dialog and returns the chosen path.</summary>
    public Func<Task<string?>>? PickFile { get; set; }

    /// <summary>Set by the window: shows a folder dialog and returns the chosen path (the Work type's sync folder).</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Computer types and applying one (ADR 22); null skips the type step.</summary>
    public ComputerTypeService? Types { get; init; }

    /// <summary>This PC's place in a device group (ADR 22); null skips the link step.</summary>
    public SyncIdentityService? Identity { get; init; }

    /// <summary>The daily focus goal the first run asks for; null skips saving it.</summary>
    public GoalService? Goal { get; init; }

    /// <summary>Work hours on this computer (ADR 28), offered with the Personal type; null hides the offer.</summary>
    public WorkHoursGuard? WorkHours { get; init; }

    public bool HasWorkHours => WorkHours is not null;

    /// <summary>The first run's three questions instead of every page.</summary>
    [ObservableProperty]
    public partial bool IsQuick { get; set; }

    /// <summary>"Guard work hours here", offered with the Personal type.</summary>
    [ObservableProperty]
    public partial bool GuardWorkHours { get; set; }

    /// <summary>Minutes of focus a day (the Goal page): 30, 60, 120 or 240.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoal30), nameof(IsGoal60), nameof(IsGoal120), nameof(IsGoal240))]
    public partial int GoalMinutes { get; set; } = 60;

    public bool IsGoal30
    {
        get => GoalMinutes == 30;
        set => ChooseGoal(30, value);
    }

    public bool IsGoal60
    {
        get => GoalMinutes == 60;
        set => ChooseGoal(60, value);
    }

    public bool IsGoal120
    {
        get => GoalMinutes == 120;
        set => ChooseGoal(120, value);
    }

    public bool IsGoal240
    {
        get => GoalMinutes == 240;
        set => ChooseGoal(240, value);
    }

    /// <summary>Opens on the first run's three questions.</summary>
    public void BeginQuick()
    {
        IsQuick = true;
        GoalMinutes = Goal?.Current.DailyMinutes ?? 60;
        if (Types is not null)
        {
            SelectedType = Types.Current ?? ComputerType.Personal;
            RefreshTypeSummary();
        }

        Step = VisibleSteps()[0];
    }

    private void ChooseGoal(int minutes, bool selected)
    {
        if (selected)
        {
            GoalMinutes = minutes;
        }
    }

    // Joined a device group in the link step: the profiles come from the other devices, so "Bring your blocks" is skipped.
    private bool _joined;

    /// <summary>"I have AccountabiliBuddy on another device": a link code to join with.</summary>
    [ObservableProperty]
    public partial bool HasOtherDevice { get; set; }

    [ObservableProperty]
    public partial string? LinkCode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkType), nameof(IsPersonalType), nameof(IsLocalOnlyType))]
    public partial ComputerType SelectedType { get; set; }

    public bool IsPersonalType
    {
        get => SelectedType == ComputerType.Personal;
        set
        {
            if (value)
            {
                SelectedType = ComputerType.Personal;
            }
        }
    }

    public bool IsWorkType
    {
        get => SelectedType == ComputerType.Work;
        set
        {
            if (value)
            {
                SelectedType = ComputerType.Work;
            }
        }
    }

    public bool IsLocalOnlyType
    {
        get => SelectedType == ComputerType.LocalOnly;
        set
        {
            if (value)
            {
                SelectedType = ComputerType.LocalOnly;
            }
        }
    }

    /// <summary>"What this computer will do" for the selected type, in plain words (to show IT if needed).</summary>
    public ObservableCollection<string> TypeSummary { get; } = new();

    partial void OnSelectedTypeChanged(ComputerType value) => RefreshTypeSummary();

    /// <summary>The Work type asks for the sync folder (e.g. inside Google Drive).</summary>
    [RelayCommand]
    private async Task ChooseSyncFolderAsync()
    {
        if (PickFolder is null || Types is null || await PickFolder() is not string path)
        {
            return;
        }

        await Types.SetFolderAsync(path);
        RefreshTypeSummary();
    }

    private void RefreshTypeSummary()
    {
        TypeSummary.Clear();
        if (Types is null)
        {
            return;
        }

        foreach (string line in ComputerTypeSummary.Lines(ComputerTypePreset.For(SelectedType), Types.Sync))
        {
            TypeSummary.Add(line);
        }
    }

    public event EventHandler? CloseRequested;

    public ObservableCollection<DetectedSourceRowViewModel> Sources { get; } = new();

    public ObservableCollection<UsageSuggestionRowViewModel> SiteSuggestions { get; } = new();

    public ObservableCollection<UsageSuggestionRowViewModel> AppSuggestions { get; } = new();

    public ObservableCollection<string> SuggestionTargets { get; } = new();

    public ObservableCollection<PlannedProfileRowViewModel> PlannedProfiles { get; } = new();

    public ObservableCollection<string> Notes { get; } = new();

    public bool HasUsage => SiteSuggestions.Count + AppSuggestions.Count > 0;

    public bool CanCopyUsageToHistory => _privacy.Current.ActivityLogEnabled && _coldTurkeyFiles is not null;

    public bool HasSources => Sources.Count > 0;

    public bool HasPlannedProfiles => PlannedProfiles.Count > 0;

    public bool HasNotes => Notes.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsLinkFirst), nameof(IsComputerType), nameof(IsImport), nameof(IsUsage), nameof(IsReview), nameof(IsBrowsers),
        nameof(IsBasics), nameof(IsGoal), nameof(IsDone), nameof(StepTitle), nameof(StepNumberText), nameof(NextText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    public partial AssistantStep Step { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(ImportFileCommand), nameof(AddPasteCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>True during a focus session: nothing can be imported.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial string PasteText { get; set; }

    [ObservableProperty]
    public partial string PasteName { get; set; }

    [ObservableProperty]
    public partial string? SelectedSuggestionTarget { get; set; }

    [ObservableProperty]
    public partial bool CopyUsageToHistory { get; set; }

    [ObservableProperty]
    public partial string WeekPreview { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    public bool IsWelcome => Step == AssistantStep.Welcome;

    public bool IsLinkFirst => Step == AssistantStep.LinkFirst;

    public bool IsComputerType => Step == AssistantStep.ComputerType;

    public bool IsImport => Step == AssistantStep.Import;

    public bool IsUsage => Step == AssistantStep.Usage;

    public bool IsReview => Step == AssistantStep.Review;

    public bool IsBrowsers => Step == AssistantStep.Browsers;

    public bool IsBasics => Step == AssistantStep.Basics;

    public bool IsGoal => Step == AssistantStep.Goal;

    public bool IsDone => Step == AssistantStep.Done;

    public string StepTitle => Step switch
    {
        AssistantStep.Welcome => "Welcome to AccountabiliBuddy",
        AssistantStep.LinkFirst => "Link to your other devices?",
        AssistantStep.ComputerType => "What kind of computer is this?",
        AssistantStep.Import => "Bring your blocks",
        AssistantStep.Usage => "Your usage",
        AssistantStep.Review => "Review",
        AssistantStep.Browsers => "Browsers",
        AssistantStep.Basics => "Basics",
        AssistantStep.Goal => "How much focus a day?",
        _ => "All set",
    };

    public string StepNumberText
    {
        get
        {
            List<AssistantStep> steps = VisibleSteps();
            return $"Step {steps.IndexOf(Step) + 1} of {steps.Count}";
        }
    }

    public string NextText => Step switch
    {
        AssistantStep.Welcome => "Get started",
        AssistantStep.Review => PlannedProfiles.Any(row => row.IsIncluded) ? "Import" : "Skip import",
        AssistantStep.Basics => "Finish",
        AssistantStep.Goal => "Finish",
        AssistantStep.Done => "Close",
        _ => "Next",
    };

    private bool CanGoNext => !IsBusy && !(IsLocked && Step == AssistantStep.Review && PlannedProfiles.Any(row => row.IsIncluded));

    // Once the import is saved, going back to it would import it twice.
    private bool CanGoBack => !IsBusy && Step != VisibleSteps()[0] && Step != AssistantStep.Done
                              && !(_hasImported && Step == AssistantStep.Browsers);

    private bool CanEditSources => !IsBusy;

    public void Dispose()
    {
        _sessions.StateChanged -= OnSessionStateChanged;
        foreach (PlannedProfileRowViewModel row in PlannedProfiles)
        {
            row.PropertyChanged -= OnPlannedProfileChanged;
        }

        _coldTurkeyFiles?.Dispose();
        Backup.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        StatusMessage = null;
        switch (Step)
        {
            case AssistantStep.Welcome when Identity is not null:
                Step = AssistantStep.LinkFirst;
                break;
            case AssistantStep.Welcome:
                await GoToTypeOrImportAsync();
                break;
            case AssistantStep.LinkFirst when IsQuick:
                if (await JoinIfAskedAsync())
                {
                    GoToNextQuickStep();
                }

                break;
            case AssistantStep.LinkFirst:
                if (await JoinIfAskedAsync())
                {
                    await GoToTypeOrImportAsync();
                }

                break;
            case AssistantStep.ComputerType:
                if (Types is not null)
                {
                    await Types.ApplyAsync(SelectedType);
                }

                if (WorkHours is not null && GuardWorkHours && SelectedType == ComputerType.Personal)
                {
                    await WorkHours.TurnOnAsync();
                }

                if (IsQuick)
                {
                    GoToNextQuickStep();
                }
                else
                {
                    await GoToImportAsync();
                }

                break;
            case AssistantStep.Goal:
                if (Goal is not null)
                {
                    await Goal.SetAsync(GoalMinutes);
                }

                Summary = $"Aiming for {GoalText(GoalMinutes)} of focus a day.";
                Step = AssistantStep.Done;
                break;
            case AssistantStep.Import:
                if (HasUsage)
                {
                    RefreshSuggestionTargets();
                    Step = AssistantStep.Usage;
                }
                else
                {
                    BuildPlan();
                    Step = AssistantStep.Review;
                }

                break;
            case AssistantStep.Usage:
                BuildPlan();
                Step = AssistantStep.Review;
                break;
            case AssistantStep.Review:
                await ImportAsync();
                Step = AssistantStep.Browsers;
                break;
            case AssistantStep.Browsers:
                Step = AssistantStep.Basics;
                break;
            case AssistantStep.Basics:
                Step = AssistantStep.Done;
                break;
            default:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    // "I have AccountabiliBuddy on another device" joins its group. True to go on (joined, or not asked); false when the
    // code didn't work or linking is locked (the step stays, with why).
    private async Task<bool> JoinIfAskedAsync()
    {
        if (!HasOtherDevice || Identity is null)
        {
            return true;
        }

        if (IsLocked)
        {
            StatusMessage = "Linking is locked while a focus session you started is running.";
            return false;
        }

        if (!await Identity.JoinAsync(LinkCode?.Trim() ?? string.Empty))
        {
            StatusMessage = "That isn't a link code: on your other device, open Devices → Link a device.";
            return false;
        }

        _joined = true;
        return true;
    }

    private async Task GoToTypeOrImportAsync()
    {
        if (Types is not null)
        {
            SelectedType = Types.Current ?? ComputerType.Personal;
            RefreshTypeSummary();
            Step = AssistantStep.ComputerType;
            return;
        }

        await GoToImportAsync();
    }

    // Joined a group: the profiles come from the other devices, so "Bring your blocks" is skipped.
    private async Task GoToImportAsync()
    {
        if (_joined)
        {
            Step = AssistantStep.Browsers;
            return;
        }

        Step = AssistantStep.Import;
        if (!_hasScanned)
        {
            await ScanAsync();
        }
    }

    private void GoToNextQuickStep()
    {
        List<AssistantStep> steps = VisibleSteps();
        int index = steps.IndexOf(Step);
        Step = index >= 0 && index + 1 < steps.Count ? steps[index + 1] : AssistantStep.Done;
    }

    private static string GoalText(int minutes)
    {
        return minutes < 60 ? $"{minutes} minutes" : minutes == 60 ? "1 hour" : $"{minutes / 60} hours";
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        StatusMessage = null;
        List<AssistantStep> steps = VisibleSteps();
        int index = steps.IndexOf(Step);
        if (index > 0)
        {
            Step = steps[index - 1];
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanEditSources))]
    private async Task ImportFileAsync()
    {
        string? path = PickFile is null ? null : await PickFile();
        if (path is null)
        {
            return;
        }

        string fileName = Path.GetFileName(path);
        try
        {
            string text = await File.ReadAllTextAsync(path);
            IReadOnlyList<ImportedBlockList> lists = LeechBlockOptionsReader.ReadExportFile(text);
            int sites = lists.Sum(list => list.BlockEntries.Count);
            AddSource(new DetectedImport(ImportSource.LeechBlock, $"LeechBlock file ({fileName})", lists,
                $"{lists.Count} block set(s), {sites} site(s)", lists.Count == 0 ? "No block sets with sites in it." : null));
            StatusMessage = $"Added {fileName}.";
        }
        catch (FormatException ex)
        {
            StatusMessage = $"{fileName}: {ex.Message} Only LeechBlock option files can be imported here; paste other lists below.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"{fileName} couldn't be read: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanEditSources))]
    private void AddPaste()
    {
        List<string> entries = SplitPaste(PasteText);
        if (entries.Count == 0)
        {
            StatusMessage = "Paste at least one site or app first.";
            return;
        }

        string name = PasteName.Trim().Length > 0 ? PasteName.Trim() : "Pasted list";
        var list = new ImportedBlockList(ImportSource.Pasted, name, entries, Array.Empty<string>(), Array.Empty<WeekRange>(), null, Array.Empty<string>());
        AddSource(new DetectedImport(ImportSource.Pasted, name, new[] { list }, $"{entries.Count} site(s)/app(s), started by hand", null));
        PasteText = string.Empty;
        PasteName = "Pasted list";
        StatusMessage = $"Added '{name}'.";
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        StatusMessage = "Looking for other blockers on this PC…";
        try
        {
            ColdTurkeyDataFiles? coldTurkey = null;
            string? copyProblem = null;
            IReadOnlyList<DetectedImport> detected = await Task.Run(() =>
            {
                try
                {
                    coldTurkey = _locator.CopyColdTurkeyData();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    copyProblem = ex.Message;
                }

                return BlockerSourceScanner.Scan(coldTurkey, _locator.FindBlockerExtensions());
            });
            _coldTurkeyFiles = coldTurkey;
            _hasScanned = true;

            if (copyProblem is not null)
            {
                AddSource(new DetectedImport(ImportSource.ColdTurkey, "Cold Turkey", Array.Empty<ImportedBlockList>(),
                    "Couldn't open its data", $"{copyProblem} Copy its lists from Cold Turkey and paste them below instead."));
            }

            foreach (DetectedImport source in detected)
            {
                AddSource(source);
            }

            await LoadSuggestionsAsync();
            StatusMessage = Sources.Count == 0
                ? "No other blockers found on this PC. You can import a LeechBlock file or paste a list from any blocker."
                : null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSuggestionsAsync()
    {
        if (_coldTurkeyFiles is null)
        {
            return;
        }

        DateTimeOffset since = _time.GetUtcNow() - SuggestionPeriod;
        ColdTurkeyDataFiles files = _coldTurkeyFiles;
        try
        {
            (IReadOnlyList<ImportedUsage> sites, IReadOnlyList<ImportedUsage> apps) = await Task.Run(() => (
                files.BrowserStatsDatabase is null ? Array.Empty<ImportedUsage>() : ColdTurkeyUsageReader.TopSites(files.BrowserStatsDatabase, since, SuggestionCount),
                files.AppStatsDatabase is null ? Array.Empty<ImportedUsage>() : ColdTurkeyUsageReader.TopApps(files.AppStatsDatabase, since, SuggestionCount)));
            foreach (ImportedUsage site in sites)
            {
                SiteSuggestions.Add(new UsageSuggestionRowViewModel(site));
            }

            foreach (ImportedUsage app in apps)
            {
                AppSuggestions.Add(new UsageSuggestionRowViewModel(app));
            }
        }
        catch (FormatException)
        {
            // No suggestions; the usage page is skipped.
        }

        OnPropertyChanged(nameof(HasUsage));
        OnPropertyChanged(nameof(CanCopyUsageToHistory));
        OnPropertyChanged(nameof(StepNumberText));
    }

    private void RefreshSuggestionTargets()
    {
        string? previous = SelectedSuggestionTarget;
        SuggestionTargets.Clear();
        SuggestionTargets.Add(NewProfileTarget);
        foreach (ImportedBlockList list in SelectedLists().Where(list => !list.IsEmpty))
        {
            SuggestionTargets.Add(list.Name);
        }

        SelectedSuggestionTarget = previous is not null && SuggestionTargets.Contains(previous) ? previous : NewProfileTarget;
    }

    /// <summary>The lists of the ticked sources, empty ones included so the review page says why they're left out.</summary>
    private List<ImportedBlockList> SelectedLists()
    {
        return Sources.Where(row => row.IsSelected && row.CanSelect)
            .SelectMany(row => row.Detected.Lists)
            .ToList();
    }

    private void BuildPlan()
    {
        List<ImportedBlockList> lists = SelectedLists();
        List<string> picked = SiteSuggestions.Concat(AppSuggestions).Where(row => row.IsSelected).Select(row => row.Entry).ToList();
        if (picked.Count > 0)
        {
            int target = lists.FindIndex(list => list.Name == SelectedSuggestionTarget);
            if (target >= 0)
            {
                lists[target] = lists[target] with { BlockEntries = lists[target].BlockEntries.Concat(picked).ToList() };
            }
            else
            {
                lists.Add(new ImportedBlockList(ImportSource.ColdTurkey, "Top distractions", picked, Array.Empty<string>(),
                    Array.Empty<WeekRange>(), null, Array.Empty<string>()));
            }
        }

        _plan = ImportPlanner.Plan(lists, _configuration.Editing, GracePeriod);

        foreach (PlannedProfileRowViewModel row in PlannedProfiles)
        {
            row.PropertyChanged -= OnPlannedProfileChanged;
        }

        PlannedProfiles.Clear();
        foreach (PlannedProfile planned in _plan.Profiles)
        {
            var row = new PlannedProfileRowViewModel(planned);
            row.PropertyChanged += OnPlannedProfileChanged;
            PlannedProfiles.Add(row);
        }

        Notes.Clear();
        foreach (string note in _plan.Notes)
        {
            Notes.Add("• " + note);
        }

        OnPropertyChanged(nameof(HasPlannedProfiles));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(NextText));
        NextCommand.NotifyCanExecuteChanged();
        RefreshWeekPreview();
    }

    private void OnPlannedProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshWeekPreview();
        OnPropertyChanged(nameof(NextText));
        NextCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Imported schedules aren't copied (ADR 18): the blocker they came from keeps enforcing them.</summary>
    private void RefreshWeekPreview()
    {
        WeekPreview = "Schedules stay with the blocker they came from, which keeps enforcing them. Plan your days in the Day plan tab.";
    }

    private async Task ImportAsync()
    {
        List<PlannedProfileRowViewModel> included = PlannedProfiles.Where(row => row.IsIncluded).ToList();
        if (included.Count == 0 && !CopyUsageToHistory)
        {
            Summary = "Nothing was imported. You can run this assistant again from the Setup tab.";
            return;
        }

        IsBusy = true;
        var summary = new List<string>();
        try
        {
            if (included.Count > 0)
            {
                List<PlannedProfile> profiles = included
                    .Select(row => row.Planned with { Profile = row.Planned.Profile with { Name = NameOf(row) } })
                    .ToList();
                var plan = new ImportPlan(profiles, _plan.Notes);
                PendingChanges? pending = await _configuration.UpdateAsync(document => plan.ApplyTo(document));
                _hasImported = true;
                summary.Add($"Imported {profiles.Count} profile(s).");
                if (pending is not null)
                {
                    summary.Add("Some of it loosens your setup, so it takes effect after the countdown (see the Profiles tab).");
                }
            }

            if (CopyUsageToHistory && CanCopyUsageToHistory)
            {
                int copied = await CopyUsageAsync();
                summary.Add($"Copied {copied} stretch(es) of Cold Turkey usage into History.");
            }
        }
        finally
        {
            IsBusy = false;
        }

        Summary = string.Join(" ", summary);
    }

    private async Task<int> CopyUsageAsync()
    {
        ColdTurkeyDataFiles files = _coldTurkeyFiles!;
        DateTimeOffset since = _time.GetUtcNow() - HistoryPeriod;
        try
        {
            IReadOnlyList<ActivitySegment> segments = await Task.Run(() =>
                ColdTurkeyUsageReader.ReadSegments(files.BrowserStatsDatabase, files.AppStatsDatabase, since));
            foreach (ActivitySegment segment in segments)
            {
                await _activityLog.RecordSegmentAsync(segment);
            }

            return segments.Count;
        }
        catch (FormatException)
        {
            return 0;
        }
    }

    private static string NameOf(PlannedProfileRowViewModel row)
    {
        string name = row.Name.Trim();
        return name.Length > 0 ? name : row.Planned.Profile.Name;
    }

    private void AddSource(DetectedImport detected)
    {
        Sources.Add(new DetectedSourceRowViewModel(detected));
        OnPropertyChanged(nameof(HasSources));
    }

    /// <summary>One entry per line; lines without a path are also split at spaces and commas.</summary>
    private static List<string> SplitPaste(string text)
    {
        var entries = new List<string>();
        foreach (string rawLine in text.Split('\n', '\r'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Contains('\\'))
            {
                entries.Add(line);
            }
            else
            {
                entries.AddRange(line.Split(new[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        return entries;
    }

    private List<AssistantStep> VisibleSteps()
    {
        return AssistantFlow.Steps(IsQuick, Identity is not null, Types is not null, HasUsage, _joined).ToList();
    }

    private void OnSessionStateChanged(object? sender, SessionState state)
    {
        IsLocked = _sessions.IsEnforcing;
    }
}
