using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Day plan tab (design part B, ADR 18, ADR 47): the categories, the named day types (each a Schedule, a Budget or a
/// free day), and the week grid that says which type each weekday follows, in each week of a repeating cycle of 1 to 8
/// weeks. Saved through <see cref="ConfigurationService"/> (a change that frees today's work waits there), locked during
/// a focus session the user started. One-off days are edited in the Exceptions tab.
/// </summary>
public sealed partial class DayPlanViewModel : ObservableObject
{
    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    // The version of the saved plan the tab last showed; a different one arriving means it changed elsewhere.
    private string? _shownVersion;

    // True while the grid is filled from a plan or from the switch, so that isn't reported back as an edit.
    private bool _loading;

    public DayPlanViewModel(ConfigurationService configuration, SessionController sessions)
    {
        _configuration = configuration;
        _sessions = sessions;
        foreach (DayOfWeek day in DayPlan.MondayFirst)
        {
            WeekRows.Add(new WeekdayRowViewModel(day));
        }

        Load(configuration.Current.DayPlan);

        // Saved elsewhere (sleep times seeded at start, a restored backup, a paired phone): show it unless something is
        // being edited; then keep the edits and say so, instead of replacing them silently.
        _configuration.Changed += (_, document) =>
        {
            if (!HasUnsavedChanges)
            {
                Load(document.DayPlan);
                return;
            }

            string version = DayPlanVersion.Of(document.DayPlan);
            if (version != _shownVersion)
            {
                _shownVersion = version;
                StatusMessage = "The plan was changed elsewhere (e.g. from your phone). Your unsaved edits are kept: Save replaces it, Revert shows the new one.";
                RefreshUnsaved();
            }
        };
        _sessions.StateChanged += (_, _) => IsLocked = _sessions.IsEnforcing;
        IsLocked = _sessions.IsEnforcing;
    }

    /// <summary>Today, for the Monday week 1 starts on when a second week is added; set by the app (the trusted clock).</summary>
    public Func<DateOnly> Today { get; init; } = () => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>The sleep goal, for a Sleep part woken by the alarm (ADR 33); set by the app.</summary>
    public Func<TimeSpan> SleepGoal { get; set; } = () => SleepSchedule.DefaultGoal;

    /// <summary>The phone's smart bedtime under a Sleep part (ADR 34): true for a workday type, false for another; null when none. Set by the app.</summary>
    public Func<bool, string?> SmartNoteFor { get; set; } = _ => null;

    /// <summary>The named day types, in the order they were saved.</summary>
    public ObservableCollection<PlanTemplateEditorViewModel> Templates { get; } = new();

    /// <summary>Monday first: the day type each weekday follows, in each week of the cycle.</summary>
    public ObservableCollection<WeekdayRowViewModel> WeekRows { get; } = new();

    public ObservableCollection<CategoryRowViewModel> Categories { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTemplate), nameof(SelectedTemplateIndex))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateTemplateCommand), nameof(DeleteTemplateCommand))]
    public partial PlanTemplateEditorViewModel? SelectedTemplate { get; set; }

    public bool HasSelectedTemplate => SelectedTemplate is not null;

    /// <summary>The day type showing (search can switch it): its place in <see cref="Templates"/>.</summary>
    public int SelectedTemplateIndex
    {
        get => SelectedTemplate is null ? -1 : Templates.IndexOf(SelectedTemplate);
        set
        {
            if (value >= 0 && value < Templates.Count)
            {
                SelectedTemplate = Templates[value];
            }
        }
    }

    /// <summary>How many weeks the cycle has (1 to <see cref="PlanCycle.MaxWeeks"/>); each has its own column in the grid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeveralWeeks), nameof(WeekCountText))]
    [NotifyCanExecuteChangedFor(nameof(AddWeekCommand), nameof(RemoveWeekCommand))]
    public partial int WeekCount { get; set; }

    /// <summary>The weeks repeat in a cycle (so week 1 has a start).</summary>
    public bool HasSeveralWeeks => WeekCount > 1;

    /// <summary>"The same every week" or "Repeats every 3 weeks".</summary>
    public string WeekCountText => WeekCount > 1 ? $"Repeats every {WeekCount} weeks" : "The same every week";

    /// <summary>The grid's column headings: "Day type" with one week, else "Week 1", "Week 2"…</summary>
    public ObservableCollection<string> WeekHeaders { get; } = new();

    /// <summary>The Monday week 1 starts on (a date picked on another weekday moves to that week's Monday).</summary>
    [ObservableProperty]
    public partial DateTime? CycleStartDate { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand), nameof(AddCategoryCommand), nameof(AddTemplateCommand),
        nameof(DuplicateTemplateCommand), nameof(DeleteTemplateCommand), nameof(AddWeekCommand), nameof(RemoveWeekCommand))]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    private bool CanEdit => !IsLocked;

    private bool CanSave => !IsLocked && HasUnsavedChanges;

    private bool CanChangeSelected => !IsLocked && SelectedTemplate is not null;

    private bool CanDeleteSelected => !IsLocked && SelectedTemplate is { CanDelete: true };

    public DayPlan Build()
    {
        var weeks = new List<IReadOnlyList<WeekdayTemplate>>();
        for (int index = 0; index < WeekCount; index++)
        {
            weeks.Add(WeekRows
                .Where(row => row.Cells[index].Template is not null)
                .Select(row => new WeekdayTemplate(row.Day, row.Cells[index].Template!.Id))
                .ToList());
        }

        // With one week the start makes no difference: keep the plan's, so adding and removing a week isn't an edit.
        DateOnly startsOn = WeekCount > 1 && CycleStartDate is DateTime date
            ? WeekSwitch.MondayOf(DateOnly.FromDateTime(date))
            : _configuration.Current.DayPlan.Cycle.StartsOn;
        return _configuration.Current.DayPlan with
        {
            Categories = Categories.Select(row => row.ToCategory()).ToList(),
            Templates = Templates.Select(template => template.ToTemplate()).ToList(),
            Cycle = new PlanCycle(weeks, startsOn),
        };
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        DayPlan plan = Build();
        if (DayPlanValidator.Validate(plan) is string problem)
        {
            StatusMessage = $"Not saved: {problem}";
            return;
        }

        await _configuration.UpdateAsync(document => document with { DayPlan = plan });
        StatusMessage = _configuration.IsReadOnly ? "Not saved: settings are read-only (see banner)." : _configuration.DayPlanWaitText ?? "Saved.";
        RefreshUnsaved();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Revert()
    {
        Load(_configuration.Current.DayPlan);
        StatusMessage = "Unsaved changes discarded.";
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddCategory()
    {
        HashSet<string> used = Categories.Select(row => row.SelectedColor.Hex).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string color = ColorChoice.Palette.FirstOrDefault(choice => !used.Contains(choice.Hex))?.Hex ?? ColorChoice.Palette[0].Hex;
        AddCategoryRow(new DayCategory(Guid.NewGuid(), "New category", color, CategoryBehavior.Watch));
        CategoriesEdited();
    }

    /// <summary>A new day type: a Schedule day with a 9:00 AM to 5:00 PM work window (the lightest thing that makes the guards work).</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddTemplate()
    {
        bool hasWorkCategory = Categories.Any(row => row.Behavior == CategoryBehavior.Watch);
        var template = new DayTemplate(
            Array.Empty<PlanPart>(),
            WorkWindow: hasWorkCategory ? new WorkWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)) : null);
        AddTemplateRow(new PlanTemplate(Guid.NewGuid(), UniqueName("New day type"), template));
        RefreshUnsaved();
    }

    [RelayCommand(CanExecute = nameof(CanChangeSelected))]
    private void DuplicateTemplate()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        PlanTemplate source = SelectedTemplate.ToTemplate();
        DayTemplate copy = source.Template with { Parts = source.Template.Parts.Select(part => part with { Id = Guid.NewGuid() }).ToList() };
        AddTemplateRow(new PlanTemplate(Guid.NewGuid(), UniqueName($"{source.Name} copy"), copy));
        RefreshUnsaved();
    }

    /// <summary>Deletes the selected day type; weekdays that followed it follow Off day. Revert brings it back before saving.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteTemplate()
    {
        if (SelectedTemplate is not { CanDelete: true } removed)
        {
            return;
        }

        PlanTemplateEditorViewModel? offDay = Templates.FirstOrDefault(template => template.Id == DayPlan.OffDayTemplateId);
        foreach (WeekCellViewModel cell in WeekRows.SelectMany(row => row.Cells))
        {
            if (cell.Template == removed)
            {
                cell.Template = offDay;
            }
        }

        int index = Templates.IndexOf(removed);
        Templates.Remove(removed);
        SelectedTemplate = Templates[Math.Clamp(index - 1, 0, Templates.Count - 1)];
        StatusMessage = $"'{removed.Name}' removed; days that followed it follow Off day now. Save to keep it.";
        RefreshUnsaved();
    }

    private bool CanAddWeek => !IsLocked && WeekCount < PlanCycle.MaxWeeks;

    private bool CanRemoveWeek => !IsLocked && WeekCount > 1;

    /// <summary>
    /// Adds a week at the end of the cycle, a copy of the last one. The first one added makes this week week 1, so nothing
    /// changes for the weeks to come until the new week's turn.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddWeek))]
    private void AddWeek()
    {
        foreach (WeekdayRowViewModel row in WeekRows)
        {
            AddCell(row, row.Cells[^1].Template);
        }

        _loading = true;
        if (WeekCount == 1)
        {
            CycleStartDate = WeekSwitch.MondayOf(Today()).ToDateTime(TimeOnly.MinValue);
        }

        WeekCount++;
        RefreshWeekHeaders();
        _loading = false;
        StatusMessage = $"Week {WeekCount} added, a copy of week {WeekCount - 1}. Use \"This is week N of {WeekCount}\" on Today to say which week this one is. Save to keep it.";
        RefreshUnsaved();
    }

    /// <summary>Removes the last week of the cycle. Revert brings it back before saving.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveWeek))]
    private void RemoveWeek()
    {
        foreach (WeekdayRowViewModel row in WeekRows)
        {
            row.Cells.RemoveAt(row.Cells.Count - 1);
        }

        _loading = true;
        WeekCount--;
        RefreshWeekHeaders();
        _loading = false;
        StatusMessage = $"Week {WeekCount + 1} removed. Save to keep it.";
        RefreshUnsaved();
    }

    partial void OnCycleStartDateChanged(DateTime? value)
    {
        if (_loading)
        {
            return;
        }

        if (value is DateTime date && date.DayOfWeek != DayOfWeek.Monday)
        {
            _loading = true;
            CycleStartDate = WeekSwitch.MondayOf(DateOnly.FromDateTime(date)).ToDateTime(TimeOnly.MinValue);
            _loading = false;
            StatusMessage = "Week 1 starts on a Monday: moved to that week's Monday.";
        }

        RefreshUnsaved();
    }

    // A new cell for one weekday and week of the grid; its day type changing is an edit.
    private void AddCell(WeekdayRowViewModel row, PlanTemplateEditorViewModel? template)
    {
        var cell = new WeekCellViewModel { Template = template };
        cell.PropertyChanged += (_, _) => RefreshUnsaved();
        row.Cells.Add(cell);
    }

    private void RefreshWeekHeaders()
    {
        WeekHeaders.Clear();
        if (WeekCount == 1)
        {
            WeekHeaders.Add("Day type");
            return;
        }

        for (int number = 1; number <= WeekCount; number++)
        {
            WeekHeaders.Add($"Week {number}");
        }
    }

    private void DeleteCategory(CategoryRowViewModel row)
    {
        if (IsLocked)
        {
            return;
        }

        DayPlan plan = _configuration.Current.DayPlan;
        bool used = Templates.SelectMany(template => template.Editor.ToTemplate().Parts)
            .Concat(plan.Templates.SelectMany(template => template.Template.Parts))
            .Concat(plan.OneOffDays.SelectMany(day => day.Template.Parts))
            .Any(part => part.CategoryId == row.Id);
        if (used)
        {
            StatusMessage = $"'{row.Name}' is used by a part of a day; give those parts another category first.";
            return;
        }

        Categories.Remove(row);
        CategoriesEdited();
    }

    private void Load(DayPlan plan)
    {
        _shownVersion = DayPlanVersion.Of(plan);
        _loading = true;
        foreach (CategoryRowViewModel row in Categories)
        {
            row.PropertyChanged -= OnCategoryRowChanged;
        }

        Categories.Clear();
        foreach (DayCategory category in plan.Categories)
        {
            AddCategoryRow(category);
        }

        Guid? selected = SelectedTemplate?.Id;
        Templates.Clear();
        foreach (PlanTemplate template in plan.Templates)
        {
            AddTemplateRow(template, select: false);
        }

        PlanTemplateEditorViewModel? Find(Guid? id) => Templates.FirstOrDefault(template => template.Id == id);
        foreach (WeekdayRowViewModel row in WeekRows)
        {
            row.Cells.Clear();
            foreach (IReadOnlyList<WeekdayTemplate> week in plan.Cycle.Weeks)
            {
                AddCell(row, Find(week.FirstOrDefault(entry => entry.Day == row.Day)?.TemplateId));
            }
        }

        WeekCount = plan.Cycle.Weeks.Count;
        RefreshWeekHeaders();
        CycleStartDate = plan.Cycle.StartsOn.ToDateTime(TimeOnly.MinValue);
        SelectedTemplate = Find(selected) ?? Templates.FirstOrDefault();
        _loading = false;
        RefreshUnsaved();
    }

    private void AddTemplateRow(PlanTemplate template, bool select = true)
    {
        var row = new PlanTemplateEditorViewModel(template, Categories.Select(category => category.ToCategory()).ToList());
        row.Editor.SleepGoal = () => SleepGoal();
        row.Editor.SmartNote = () => SmartNoteFor(_configuration.Current.DayPlan.IsWorkdayTemplate(row.Id));
        row.PropertyChanged += OnTemplateRowChanged;
        row.Editor.Changed += (_, _) => RefreshUnsaved();
        Templates.Add(row);
        if (select)
        {
            SelectedTemplate = row;
        }
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        for (int number = 2; Templates.Any(template => string.Equals(template.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)); number++)
        {
            name = $"{baseName} {number}";
        }

        return name;
    }

    private void AddCategoryRow(DayCategory category)
    {
        var row = new CategoryRowViewModel(category, DeleteCategory);
        row.PropertyChanged += OnCategoryRowChanged;
        Categories.Add(row);
    }

    private void OnTemplateRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlanTemplateEditorViewModel.Name))
        {
            RefreshUnsaved();
        }
    }

    private void OnCategoryRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        CategoriesEdited();
    }

    private void CategoriesEdited()
    {
        IReadOnlyList<DayCategory> categories = Categories.Select(row => row.ToCategory()).ToList();
        foreach (PlanTemplateEditorViewModel template in Templates)
        {
            template.Editor.SetCategories(categories);
        }

        RefreshUnsaved();
    }

    // Records holding lists compare lists by reference, so the plans are compared as JSON, in a fixed order (the
    // editor lists parts in the order of the day, the saved plan in the order they were saved).
    private void RefreshUnsaved()
    {
        if (_loading)
        {
            return;
        }

        HasUnsavedChanges = JsonSerializer.Serialize(Normalized(Build())) != JsonSerializer.Serialize(Normalized(_configuration.Current.DayPlan));
    }

    private static DayPlan Normalized(DayPlan plan)
    {
        return plan with
        {
            Templates = plan.Templates
                .Select(template => template with { Template = template.Template with { Parts = template.Template.Parts.OrderBy(part => part.Id).ToList() } })
                .ToList(),
            Cycle = plan.Cycle with
            {
                Weeks = plan.Cycle.Weeks.Select(week => (IReadOnlyList<WeekdayTemplate>)week.OrderBy(entry => entry.Day).ToList()).ToList(),
            },
        };
    }

    /// <summary>Opens "Plan a day" (ADR 37); set by the app.</summary>
    public Action? ShowPlanDay { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void PlanDay() => ShowPlanDay?.Invoke();
}
