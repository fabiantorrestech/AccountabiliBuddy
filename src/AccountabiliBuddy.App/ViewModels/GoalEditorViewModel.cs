using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AccountabiliBuddy.App.Services.Goals;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Goals;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The Edit goal and New goal dialogs (round 2 R5.4; brief §4.4, §10 Q11; change note §4; boards Goals-PC-Edit, Goals-PC-New,
/// Goals-PC-Edit-Reminders). One shell: the left nav (Edit: Basics, Target, Tracking &amp; sources, Reminders, On screen, Notes; New: the
/// five steps Basics › Target › Tracking &amp; sources › Reminders (optional) › Notes (optional)), the section on the right, and the footer
/// (Edit: the Active switch, the only place that pauses, Delete goal…, Cancel, Save; New: "Step N of 5", Back, Next, "Create now" once
/// Basics and Target are valid, Create on the last step). Every change goes through <see cref="GoalsService"/>: other goals' target
/// changes apply at once (§10 Q6); Deep work's targets go through its waiting rule and its sources stay locked; Deep work can't be
/// paused or deleted. Names, notes and reasons are private text: on screen only, never logged.
/// </summary>
public sealed partial class GoalEditorViewModel : ObservableObject
{
    public const int MaxNameLength = 80;

    private static readonly GoalKind[] Kinds = [GoalKind.Habit, GoalKind.Amount, GoalKind.Time, GoalKind.Milestone];
    private static readonly GoalEditSection[] NewSteps =
        [GoalEditSection.Basics, GoalEditSection.Target, GoalEditSection.TrackingAndSources, GoalEditSection.Reminders, GoalEditSection.Notes];

    private static readonly string[] IconNames =
    [
        "translate", "menu_book", "school", "work", "timer", "flag", "fitness_center", "directions_run", "footprint", "monitor_weight",
        "self_improvement", "water_drop", "bedtime", "code", "music_note", "palette", "savings", "home", "track_changes",
    ];

    private readonly GoalsService _goals;
    private readonly GoalSourceDataService? _data;
    private readonly GoalEditorLookups _lookups;
    private readonly Goal? _stored;
    private readonly DateOnly _today;
    private readonly TimeZoneInfo _zone;
    private bool _loading;
    private bool _basicsVisited;

    /// <param name="goal">The goal to edit; null for New goal.</param>
    /// <param name="start">The section to open at (Edit only; New goal starts at Basics).</param>
    public GoalEditorViewModel(
        GoalsService goals, GoalSourceDataService? data, GoalEditorLookups lookups, Goal? goal, GoalEditSection start, DateOnly today, TimeZoneInfo zone,
        bool focusActive = false)
    {
        _goals = goals;
        _data = data;
        _lookups = lookups;
        _today = today;
        _zone = zone;
        _stored = goal is null ? null : goal.IsDeepWork && goals.DeepWork is Goal deepWork ? deepWork : goal;
        IsNew = goal is null;
        FocusActive = focusActive;
        Target = new GoalTargetEditorViewModel(lookups.IsProductaBuddy, kind => goals.CancelDeepWorkPendingAsync(kind));
        Sources = new GoalSourcesEditorViewModel();
        Reminders = new GoalRemindersEditorViewModel(lookups.IsProductaBuddy);
        Icons = IconNames.Select(name => new GoalChoice(name, name.Replace('_', ' '), GoalIcons.Of(name))).ToList();
        Colours = TaskCategory.Presets.Select(colour => new GoalChoice(colour, colour, Brush: GoalIcons.Brush(colour))).ToList();
        Areas = new ObservableCollection<GoalChoice>();
        BuildNav();
        Load();
        Target.Changed += (_, _) => Redraw();
        Sources.Changed += (_, _) => Redraw();
        Reminders.Changed += (_, _) => Redraw();
        GoTo(IsNew ? GoalEditSection.Basics : start);
    }

    /// <summary>The dialog should close (saved, created, deleted, or Cancel).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Delete goal… asks first (the view shows the question).</summary>
    public event EventHandler<GoalConfirmRequest>? ConfirmRequested;

    /// <summary>Basics' "+ New area" (the view opens Edit area, then <see cref="AreaAdded"/>).</summary>
    public event EventHandler? NewAreaRequested;

    public bool IsNew { get; }

    public bool IsEdit => !IsNew;

    /// <summary>Opened from the menu's Pause goal… / Resume goal…: the Active switch takes the keyboard.</summary>
    public bool FocusActive { get; }

    public bool IsDeepWork => _stored?.IsDeepWork == true;

    public bool CanChangeKind => !IsDeepWork;

    /// <summary>The goal made by Create / Create now; null until then.</summary>
    public Guid? CreatedId { get; private set; }

    public ObservableCollection<GoalEditorNavItem> Nav { get; } = [];

    public GoalTargetEditorViewModel Target { get; }

    public GoalSourcesEditorViewModel Sources { get; }

    public GoalRemindersEditorViewModel Reminders { get; }

    public IReadOnlyList<GoalChoice> Icons { get; }

    public IReadOnlyList<GoalChoice> Colours { get; }

    public ObservableCollection<GoalChoice> Areas { get; }

    public IReadOnlyList<string> KindChoices { get; } = ["Habit", "Amount", "Time", "Milestone"];

    /// <summary>What the chosen kind means, under the kind switch.</summary>
    public string KindHint => KindIndex switch
    {
        0 => "Yes or no each day (or N times a week): ticked by hand, or ticking itself once focus time reaches a threshold.",
        1 => "A value over time: steps, a count, a weight. A day's value is your own edit, else its first reading.",
        2 => "Minutes: focus on a label or #tag, calendar time, time added by hand. Never counts for a guard.",
        _ => "A milestone you mark done by hand; its own task list is coming soon.",
    };

    [ObservableProperty]
    public partial GoalEditSection Section { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int KindIndex { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedIcon { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedColour { get; set; }

    [ObservableProperty]
    public partial GoalChoice? SelectedArea { get; set; }

    [ObservableProperty]
    public partial bool ShowOnToday { get; set; }

    /// <summary>On screen: the extra tiles follow Goals Options (the default) instead of this goal's own switches.</summary>
    [ObservableProperty]
    public partial bool UseDefaultTiles { get; set; } = true;

    [ObservableProperty]
    public partial bool StreakTile { get; set; }

    [ObservableProperty]
    public partial bool TotalTile { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    /// <summary>The links found in the notes, previewed under them (and on the detail's Notes card).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> NoteLinks { get; set; } = [];

    /// <summary>The footer's Active switch (Edit only): off = paused from today; on again = counts from today.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; } = true;

    /// <summary>What the footer says after a save that left something waiting, or why it can't save.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string HeaderTitle => IsNew ? "New goal" : $"Edit goal · {(Name.Trim().Length > 0 ? Name.Trim() : _stored?.Name)}";

    public string HeaderSub
    {
        get
        {
            Goal draft = Draft();
            string area = SelectedArea?.Text ?? string.Empty;
            var parts = new List<string>();
            if (IsNew && Name.Trim().Length > 0)
            {
                parts.Add(Name.Trim());
            }

            if (area.Length > 0)
            {
                parts.Add(area);
            }

            parts.Add(IsDeepWork ? "built in" : GoalTexts.KindText(draft));
            if (!IsNew)
            {
                parts.Add(Target.Summary(draft, _today));
            }

            return string.Join(" · ", parts);
        }
    }

    public MaterialIconKind HeaderIcon => GoalIcons.Of(SelectedIcon?.Key);

    public IBrush HeaderBrush => GoalIcons.Brush(SelectedColour?.Key);

    public IBrush HeaderSoftBrush => GoalIcons.SoftBrush(SelectedColour?.Key, Brushes.Transparent);

    public bool IsBasics => Section == GoalEditSection.Basics;

    public bool IsTargetSection => Section == GoalEditSection.Target;

    public bool IsSources => Section == GoalEditSection.TrackingAndSources;

    public bool IsRemindersSection => Section == GoalEditSection.Reminders;

    public bool IsOnScreen => Section == GoalEditSection.OnScreen;

    public bool IsNotesSection => Section == GoalEditSection.Notes;

    public bool HasMessage => Message is not null;

    public bool HasNoteLinks => NoteLinks.Count > 0;

    public bool CanPause => IsEdit && !IsDeepWork;

    public bool CanDelete => IsEdit && !IsDeepWork;

    public string ActiveNote => IsDeepWork
        ? "Deep work can't be paused: use Track time only from its menu."
        : "Turn off to pause: stops counting and reminders, keeps history";

    public string DefaultTilesText
    {
        get
        {
            GoalExtraTiles tiles = _goals.Store.PageSettings.ExtraTiles ?? GoalExtraTiles.None;
            string shown = tiles is { Streak: true, Total: true } ? "Streak and Total" : tiles.Streak ? "Streak" : tiles.Total ? "Total" : "no extra tiles";
            return $"Same as Goals Options ({shown})";
        }
    }

    // ---- New goal steps ----

    public int StepNumber => Math.Max(1, Array.IndexOf(NewSteps, Section) + 1);

    public string StepText => $"Step {StepNumber} of {NewSteps.Length}";

    public bool CanGoBack => IsNew && StepNumber > 1;

    public bool IsLastStep => IsNew && StepNumber == NewSteps.Length;

    public bool ShowsNext => IsNew && !IsLastStep;

    public string NextText => StepNumber < NewSteps.Length ? $"Next: {NavTitle(NewSteps[StepNumber])}" : "Create";

    /// <summary>"Create now" after Target (brief §10 Q11): Basics and Target are valid and the user is past Basics.</summary>
    public bool CanCreateNow => IsNew && StepNumber >= 2 && !IsLastStep && BasicsProblem() is null && Target.Problem is null;

    /// <summary>Called after the view's Edit area made a new area: it joins the list, chosen.</summary>
    public void AreaAdded(Guid areaId)
    {
        RefreshAreas();
        SelectedArea = Areas.FirstOrDefault(choice => choice.Key == areaId.ToString()) ?? SelectedArea;
    }

    [RelayCommand]
    private void GoTo(GoalEditSection section)
    {
        if (IsNew)
        {
            int at = Array.IndexOf(NewSteps, section);
            if (at < 0)
            {
                return;
            }

            // Later steps open once Basics and Target are valid (they can be skipped from then on).
            if (at >= 2 && (BasicsProblem() is not null || Target.Problem is not null))
            {
                Message = BasicsProblem() ?? Target.Problem;
                return;
            }

            if (at >= 1)
            {
                _basicsVisited = true;
            }
        }

        Message = null;
        Section = section;
    }

    [RelayCommand]
    private void Next()
    {
        if (!IsNew || IsLastStep)
        {
            return;
        }

        string? problem = StepProblem(Section);
        if (problem is not null)
        {
            Message = problem;
            return;
        }

        GoTo(NewSteps[StepNumber]);
    }

    [RelayCommand]
    private void Back()
    {
        if (CanGoBack)
        {
            Message = null;
            Section = NewSteps[StepNumber - 2];
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void NewArea()
    {
        NewAreaRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>"Create now" (after Target) and Create (last step): adds the goal and its reminders; false when something is missing.</summary>
    [RelayCommand]
    public async Task<bool> CreateAsync()
    {
        if (!IsNew || CreatedId is not null || IsBusy)
        {
            return false;
        }

        string? problem = BasicsProblem() ?? Target.Problem ?? Sources.Problem ?? Reminders.Problem;
        if (problem is not null)
        {
            Message = problem;
            return false;
        }

        IsBusy = true;
        try
        {
            Goal goal = Draft();
            if (goal.Sources.Count == 0)
            {
                // A goal with nothing counting toward it can still be ticked or logged by hand.
                goal = goal with { Sources = [new GoalSourceRule(Guid.NewGuid(), GoalSourceType.Hand)] };
            }

            double position = _goals.Lists.LiveGoals.Where(item => item.AreaId == goal.AreaId).Select(item => item.Position).DefaultIfEmpty(0).Max() + 1;
            goal = goal with { Position = position };
            GoalChangeResult added = await _goals.AddGoalAsync(goal, this);
            if (added != GoalChangeResult.Applied)
            {
                Message = added == GoalChangeResult.NotAvailable ? "Goals aren't ready on this PC yet." : "This goal can't be made as it is.";
                return false;
            }

            CreatedId = goal.Id;
            foreach (GoalReminder reminder in Reminders.Build(goal.Id))
            {
                await _goals.SaveReminderAsync(reminder, this);
            }

            CloseRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Save (Edit): the goal's fields, its Active switch (pause / resume from today), its reminders; Deep work's daily and weekly targets
    /// through its waiting rule. Closes when everything applied; a change that waits or can't happen now is said in the footer and the
    /// dialog stays open (Cancel closes it).
    /// </summary>
    [RelayCommand]
    public async Task<bool> SaveAsync()
    {
        if (IsNew || _stored is null || IsBusy)
        {
            return false;
        }

        string? problem = BasicsProblem() ?? Target.Problem ?? Sources.Problem ?? Reminders.Problem;
        if (problem is not null)
        {
            Message = problem;
            return false;
        }

        IsBusy = true;
        var notes = new List<string>();
        try
        {
            Goal? current = _goals.Lists.Goals.FirstOrDefault(goal => goal.Id == _stored.Id && goal.IsLive);
            if (current is null)
            {
                Message = "This goal was deleted meanwhile.";
                return false;
            }

            Goal draft = Draft();
            GoalChangeResult updated = await _goals.UpdateGoalAsync(
                IsDeepWork
                    ? current with
                    {
                        Name = draft.Name,
                        Icon = draft.Icon,
                        Color = draft.Color,
                        AreaId = draft.AreaId,
                        Position = draft.AreaId == current.AreaId ? current.Position : NextPosition(draft.AreaId),
                        ShowOnToday = draft.ShowOnToday,
                        ExtraTiles = draft.ExtraTiles,
                        Notes = draft.Notes,
                    }
                    : draft with
                    {
                        Position = draft.AreaId == current.AreaId ? current.Position : NextPosition(draft.AreaId),
                        Pauses = current.Pauses,
                        CreatedAt = current.CreatedAt,
                    },
                this);
            if (updated is GoalChangeResult.NotAllowed or GoalChangeResult.NotAvailable)
            {
                Message = updated == GoalChangeResult.NotAvailable ? "Goals aren't ready on this PC yet." : "These changes can't be saved for this goal.";
                return false;
            }

            if (IsDeepWork)
            {
                notes.AddRange(await SaveDeepWorkTargetsAsync());
            }
            else
            {
                bool pausedToday = current.IsPausedOn(_today);
                if (!IsActive && !pausedToday)
                {
                    await _goals.PauseGoalAsync(current.Id, this);
                }
                else if (IsActive && pausedToday)
                {
                    await _goals.ResumeGoalAsync(current.Id, this);
                }
            }

            await SaveRemindersAsync(current.Id);
        }
        finally
        {
            IsBusy = false;
        }

        if (notes.Count > 0)
        {
            Message = "Saved. " + string.Join(" ", notes);
            return true;
        }

        Message = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Delete goal… (Edit, not Deep work): asks first; deleting closes the dialog.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (!CanDelete || _stored is null)
        {
            return;
        }

        Guid id = _stored.Id;
        ConfirmRequested?.Invoke(this, GoalConfirmPrompts.DeleteGoal(_goals, id, () => CloseRequested?.Invoke(this, EventArgs.Empty)));
    }

    // ---- loading and drawing ----

    private void Load()
    {
        _loading = true;
        try
        {
            RefreshAreas();
            Goal goal = _stored ?? NewDraft();
            Name = IsNew ? string.Empty : goal.Name;
            KindIndex = Math.Max(0, Array.IndexOf(Kinds, goal.Kind));
            SelectedIcon = Icons.FirstOrDefault(choice => choice.Key == goal.Icon) ?? new GoalChoice(goal.Icon, goal.Icon, GoalIcons.Of(goal.Icon));
            SelectedColour = Colours.FirstOrDefault(choice => string.Equals(choice.Key, goal.Color, StringComparison.OrdinalIgnoreCase))
                ?? new GoalChoice(goal.Color, goal.Color, Brush: GoalIcons.Brush(goal.Color));
            SelectedArea = Areas.FirstOrDefault(choice => choice.Key == goal.AreaId.ToString()) ?? Areas.FirstOrDefault();
            ShowOnToday = goal.ShowOnToday;
            UseDefaultTiles = goal.ExtraTiles is null;
            StreakTile = goal.ExtraTiles?.Streak == true;
            TotalTile = goal.ExtraTiles?.Total == true;
            Notes = goal.Notes ?? string.Empty;
            IsActive = IsNew || !goal.IsPausedOn(_today);
            Target.Load(goal, IsDeepWork ? _goals.DeepWorkSettings : null, _today, _zone);
            Sources.Load(goal, _lookups);
            GoalPageSettings settings = _goals.Store.PageSettings;
            IEnumerable<GoalReminder> reminders = IsNew ? [] : _goals.Lists.Reminders.Where(item => item.GoalId == goal.Id && item.IsLive);
            Reminders.Load(reminders, settings.ReminderDefaults, _today, GoalTexts.Threshold(goal) is double minutes ? GoalTexts.Minutes(minutes) : null);
        }
        finally
        {
            _loading = false;
        }

        Redraw();
    }

    private Goal NewDraft()
    {
        Guid area = _goals.Lists.Areas.Where(item => item.IsLive).OrderBy(item => item.Position).Select(item => item.Id).FirstOrDefault();
        return new Goal(Guid.NewGuid(), area, string.Empty, "track_changes", TaskCategory.Palette[5], GoalKind.Habit,
            new GoalTarget(GoalPeriod.Day, null, GoalUnit.YesNo), [new GoalSourceRule(Guid.NewGuid(), GoalSourceType.Hand)], default, 0, default);
    }

    private void RefreshAreas()
    {
        string? chosen = SelectedArea?.Key;
        Areas.Clear();
        foreach (GoalArea area in _goals.Lists.Areas.Where(item => item.IsLive).OrderBy(item => item.Position))
        {
            Areas.Add(new GoalChoice(area.Id.ToString(), area.Name, GoalIcons.Of(area.Icon), GoalIcons.Brush(area.Color)));
        }

        if (chosen is not null)
        {
            SelectedArea = Areas.FirstOrDefault(choice => choice.Key == chosen);
        }
    }

    // The goal as the dialog has it now (sources and target as typed; the stored values where something can't be read yet).
    private Goal Draft()
    {
        Goal basis = _stored ?? NewDraft();
        GoalKind kind = Kinds[Math.Clamp(KindIndex, 0, Kinds.Length - 1)];
        GoalTarget target = IsDeepWork ? basis.Target : Target.BuildTarget() ?? basis.Target;
        IReadOnlyList<GoalSourceRule> sources = IsDeepWork ? basis.Sources : Sources.Build(Target.Threshold);
        Guid areaId = SelectedArea is GoalChoice area && Guid.TryParse(area.Key, out Guid parsed) ? parsed : basis.AreaId;
        string notes = Notes.Trim();
        return basis with
        {
            Name = Name.Trim(),
            Kind = IsDeepWork ? basis.Kind : kind,
            Icon = SelectedIcon?.Key ?? basis.Icon,
            Color = SelectedColour?.Key ?? basis.Color,
            AreaId = areaId,
            Target = target,
            Sources = sources,
            ShowOnToday = ShowOnToday,
            ExtraTiles = UseDefaultTiles ? null : new GoalExtraTiles(StreakTile, TotalTile),
            Notes = notes.Length == 0 ? null : notes,
        };
    }

    private void Redraw()
    {
        if (_loading)
        {
            return;
        }

        UpdateWhatItTakes();
        foreach (string name in new[]
        {
            nameof(HeaderTitle), nameof(HeaderSub), nameof(HeaderIcon), nameof(HeaderBrush), nameof(HeaderSoftBrush), nameof(CanCreateNow),
            nameof(NextText), nameof(StepText), nameof(CanGoBack), nameof(IsLastStep), nameof(ShowsNext), nameof(StepNumber),
        })
        {
            OnPropertyChanged(name);
        }

        Goal draft = Draft();
        foreach (GoalEditorNavItem item in Nav)
        {
            item.IsCurrent = item.Section == Section;
            item.Sub = NavSub(item.Section, draft);
            item.IsDone = IsNew && Array.IndexOf(NewSteps, item.Section) < StepNumber - 1 && StepProblem(item.Section) is null;
        }
    }

    private void UpdateWhatItTakes()
    {
        Goal draft = Draft();
        GoalWhatItTakes? takes = null;
        if (!IsDeepWork && draft.Target.TargetAmount is double target)
        {
            takes = IsNew || _data is null
                ? GoalWhatItTakes.Compute(target, _today, draft.Target.TargetDate, null, GoalWhatItTakes.OwnRate(draft, _today))
                : GoalWhatItTakes.For(draft, _data.Current, _today);
        }

        Target.WhatItTakes.Update(takes, draft, _today, sameGoal: true);
        Target.WhatItTakes.IsOpen = takes is not null;
    }

    private void BuildNav()
    {
        IEnumerable<GoalEditSection> sections = IsNew ? NewSteps : Enum.GetValues<GoalEditSection>();
        foreach (GoalEditSection section in sections)
        {
            Nav.Add(new GoalEditorNavItem(section, NavTitle(section), section switch
            {
                GoalEditSection.Basics => MaterialIconKind.PencilOutline,
                GoalEditSection.Target => MaterialIconKind.FlagOutline,
                GoalEditSection.TrackingAndSources => MaterialIconKind.LinkVariant,
                GoalEditSection.Reminders => MaterialIconKind.BellOutline,
                GoalEditSection.OnScreen => MaterialIconKind.ViewDashboardOutline,
                _ => MaterialIconKind.NoteTextOutline,
            }));
        }
    }

    private static string NavTitle(GoalEditSection section) => section switch
    {
        GoalEditSection.Basics => "Basics",
        GoalEditSection.Target => "Target",
        GoalEditSection.TrackingAndSources => "Tracking & sources",
        GoalEditSection.Reminders => "Reminders",
        GoalEditSection.OnScreen => "On screen",
        _ => "Notes",
    };

    private string NavSub(GoalEditSection section, Goal draft)
    {
        if (IsNew && section == Section)
        {
            return StepText;
        }

        return section switch
        {
            GoalEditSection.Basics when IsNew && _basicsVisited && BasicsProblem() is null =>
                $"{draft.Name} · {SelectedArea?.Text} · {KindChoices[Math.Clamp(KindIndex, 0, KindChoices.Count - 1)].Split(' ')[0].ToLowerInvariant()}",
            GoalEditSection.Basics => "Name, icon, colour, area",
            GoalEditSection.Target when IsNew && Target.Problem is not null => "How much, how often",
            GoalEditSection.Target => Target.Summary(draft, _today),
            GoalEditSection.TrackingAndSources => IsNew ? "What counts" : Sources.Summary,
            GoalEditSection.Reminders => IsNew && Reminders.IsEmpty ? "Optional" : Reminders.Summary,
            GoalEditSection.OnScreen => "Today card, extra tiles",
            _ => IsNew && Notes.Trim().Length == 0 ? "Optional" : "Why it matters, links",
        };
    }

    private string? StepProblem(GoalEditSection section) => section switch
    {
        GoalEditSection.Basics => BasicsProblem(),
        GoalEditSection.Target => Target.Problem,
        GoalEditSection.TrackingAndSources => Sources.Problem,
        GoalEditSection.Reminders => Reminders.Problem,
        _ => null,
    };

    private string? BasicsProblem()
    {
        string name = Name.Trim();
        if (name.Length == 0)
        {
            return "Give the goal a name.";
        }

        if (name.Length > MaxNameLength)
        {
            return $"Keep the name under {MaxNameLength} characters.";
        }

        return SelectedArea is null ? "Pick an area (or make one)." : null;
    }

    private DateTimeOffset Now => _data?.Now ?? DateTimeOffset.UtcNow;

    private double NextPosition(Guid areaId)
    {
        return _goals.Lists.LiveGoals.Where(item => item.AreaId == areaId).Select(item => item.Position).DefaultIfEmpty(0).Max() + 1;
    }

    // Deep work's targets through its waiting rule (GoalsService): what waited or couldn't start, in words.
    private async Task<List<string>> SaveDeepWorkTargetsAsync()
    {
        var notes = new List<string>();
        Goal? shown = _goals.DeepWork;
        int? dailyNow = shown?.Target.Amount is double daily ? (int)daily : null;
        int? weeklyNow = _goals.DeepWorkSettings.TrackTimeOnly ? null : _goals.DeepWorkSettings.WeeklyMinutes;
        if (Target.DeepWorkDaily is int wantedDaily && wantedDaily != dailyNow)
        {
            notes.AddRange(Note(await _goals.SetDeepWorkDailyAsync(wantedDaily), "daily", _goals.DeepWorkSettings.PendingDaily));
        }

        if (Target.DeepWorkWeekly != weeklyNow && !(Target.TrackTimeOnly && Target.DeepWorkWeekly is null))
        {
            notes.AddRange(Note(await _goals.SetDeepWorkWeeklyAsync(Target.DeepWorkWeekly), "weekly", _goals.DeepWorkSettings.PendingWeekly));
        }

        return notes;
    }

    private IEnumerable<string> Note(GoalChangeResult result, string which, GoalPendingChange? pending)
    {
        switch (result)
        {
            case GoalChangeResult.Waiting when pending is not null:
                string to = pending.Amount is double amount ? GoalTexts.Minutes(amount) : "no target";
                yield return pending.AppliesAt <= Now
                    ? $"The {which} target lowers to {to} once today's work hours are over."
                    : $"The {which} target lowers to {to} at {GoalTexts.Time(pending.AppliesAt, _zone)} (never during a session); Cancel is on Target.";
                break;
            case GoalChangeResult.Waiting:
                yield return $"Lowering the {which} target waits like any looser change.";
                break;
            case GoalChangeResult.RefusedDuringSession:
                yield return $"Lowering the {which} target can't start during a session; raising it is fine.";
                break;
            case GoalChangeResult.NotAllowed or GoalChangeResult.NotAvailable:
                yield return $"The {which} target couldn't be changed now.";
                break;
        }
    }

    private async Task SaveRemindersAsync(Guid goalId)
    {
        foreach (Guid removed in Reminders.Removed)
        {
            if (_goals.Lists.Reminders.Any(item => item.Id == removed && item.IsLive))
            {
                await _goals.DeleteReminderAsync(removed, this);
            }
        }

        foreach (GoalReminder reminder in Reminders.Build(goalId))
        {
            GoalReminder? stored = _goals.Lists.Reminders.FirstOrDefault(item => item.Id == reminder.Id && item.IsLive);
            if (stored is null || stored with { EditedAt = default, Weekdays = null } != reminder with { EditedAt = default, Weekdays = null } || !SameDays(stored, reminder))
            {
                await _goals.SaveReminderAsync(reminder, this);
            }
        }
    }

    private static bool SameDays(GoalReminder left, GoalReminder right)
    {
        return (left.Weekdays ?? []).SequenceEqual(right.Weekdays ?? []);
    }

    // ---- change hooks ----

    partial void OnSectionChanged(GoalEditSection value)
    {
        foreach (string name in new[] { nameof(IsBasics), nameof(IsTargetSection), nameof(IsSources), nameof(IsRemindersSection), nameof(IsOnScreen), nameof(IsNotesSection) })
        {
            OnPropertyChanged(name);
        }

        Redraw();
    }

    partial void OnNameChanged(string value) => Redraw();

    partial void OnKindIndexChanged(int value)
    {
        if (!_loading)
        {
            GoalKind kind = Kinds[Math.Clamp(value, 0, Kinds.Length - 1)];
            if (kind == GoalKind.Time && Target.Amount is null && Target.TargetAmount is null)
            {
                Target.Amount = 30;
            }

            Target.SetKind(kind);
        }

        OnPropertyChanged(nameof(KindHint));
        Redraw();
    }

    partial void OnSelectedIconChanged(GoalChoice? value) => Redraw();

    partial void OnSelectedColourChanged(GoalChoice? value) => Redraw();

    partial void OnSelectedAreaChanged(GoalChoice? value) => Redraw();

    partial void OnNotesChanged(string value)
    {
        NoteLinks = Regex.Matches(value, @"https?://[^\s<>""']+", RegexOptions.IgnoreCase).Select(match => match.Value.TrimEnd('.', ',', ')', ';')).Distinct().Take(8).ToList();
        Redraw();
    }

    partial void OnNoteLinksChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(HasNoteLinks));

    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));
}
