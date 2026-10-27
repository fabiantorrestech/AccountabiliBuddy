using System.Collections.ObjectModel;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Edits one day template (Workdays, Off-Days or a one-off day): a 24-hour bar whose parts are dragged, and a list
/// that shows the selected part (or all of them when none is selected). Both stay in step; the drag math is
/// <see cref="DayTemplateEditing"/>.
/// </summary>
public sealed partial class DayPlanEditorViewModel : ObservableObject
{
    public DayPlanEditorViewModel(DayTemplate template, IReadOnlyList<DayCategory> categories)
    {
        Categories = categories;
        Load(template);
    }

    public IReadOnlyList<DayCategory> Categories { get; private set; }

    /// <summary>
    /// The categories a part may pick: all of them, but on a Budget day only the non-productive ones (rest, sleep,
    /// pre-work), because its parts are the windows that don't count toward the budget (ADR 47).
    /// </summary>
    public IReadOnlyList<DayCategory> PickableCategories =>
        Mode == DayMode.Budget ? Categories.Where(category => !category.Behavior.IsWork()).ToList() : Categories;

    /// <summary>How the day is guarded (schema v12, ADR 47): the timeline, a budget of hours, or not at all.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedModeIndex), nameof(IsSchedule), nameof(IsBudget), nameof(IsFree), nameof(ShowWindow),
        nameof(ShowTimeline), nameof(ShowPartsEditor), nameof(ShowPartsInExpander), nameof(PartsTitle), nameof(PickableCategories))]
    public partial DayMode Mode { get; set; }

    /// <summary>A Schedule day that is just a work window ("9:00 AM to 5:00 PM") instead of the full timeline (C 15).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWindow), nameof(ShowTimeline), nameof(ShowPartsEditor), nameof(ShowPartsInExpander), nameof(PartsTitle))]
    public partial bool UseWorkWindow { get; set; }

    [ObservableProperty]
    public partial TimeSpan? WindowStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan? WindowEnd { get; set; }

    /// <summary>A Budget day's productive hours, in quarters of an hour.</summary>
    [ObservableProperty]
    public partial decimal? BudgetHours { get; set; }

    /// <summary>The mode picker's index (Schedule, Budget, Free: the order of <see cref="DayMode"/>).</summary>
    public int SelectedModeIndex
    {
        get => (int)Mode;
        set
        {
            if (value >= 0 && value <= (int)DayMode.Free)
            {
                Mode = (DayMode)value;
            }
        }
    }

    public static IReadOnlyList<string> ModeNames { get; } = new[] { "Schedule", "Budget", "Free day" };

    public bool IsSchedule => Mode == DayMode.Schedule;

    public bool IsBudget => Mode == DayMode.Budget;

    public bool IsFree => Mode == DayMode.Free;

    /// <summary>The two work-window times show instead of the timeline.</summary>
    public bool ShowWindow => IsSchedule && UseWorkWindow;

    /// <summary>The full timeline editor is the day's editor (Schedule without a work window).</summary>
    public bool ShowTimeline => IsSchedule && !UseWorkWindow;

    /// <summary>The bar and list show directly: a full timeline, or a Budget day's non-productive windows.</summary>
    public bool ShowPartsEditor => ShowTimeline || IsBudget;

    /// <summary>The bar and list are tucked in an expander (rest and sleep around a work window, or a free day's sleep).</summary>
    public bool ShowPartsInExpander => ShowWindow || IsFree;

    public string PartsTitle => IsBudget ? "Non-productive windows (lunch, rest, sleep): the budget is found in the waking day outside them"
        : ShowWindow ? "Lunch, rest and sleep around the work window (optional)"
        : IsFree ? "Sleep and rest on a free day (optional)"
        : "Timeline";

    /// <summary>This template's block rules (ADR 37), kept through part edits; null = the defaults.</summary>
    public BlockRules? Blocks { get; set; }

    private const int DefaultBudgetMinutes = 6 * 60;

    // True while a template is being loaded into the mode fields, so loading isn't reported back as an edit.
    private bool _loading;

    /// <summary>The sleep goal, for a Sleep part woken by the alarm (its end is bedtime plus this); 8 hours unless set.</summary>
    public Func<TimeSpan> SleepGoal { get; set; } = () => AccountabiliBuddy.Core.Engine.SleepSchedule.DefaultGoal;

    /// <summary>The phone's smart bedtime for this template's nights (ADR 34), shown under its Sleep part; null when none.</summary>
    public Func<string?> SmartNote { get; set; } = () => null;

    public ObservableCollection<PlanPartRowViewModel> Parts { get; } = new();

    /// <summary>The selected part only, or every part (in the order of the day) when none is selected.</summary>
    public ObservableCollection<PlanPartRowViewModel> ShownRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial PlanPartRowViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>What's wrong with the template (overlaps, a part under 15 minutes…); null when it can be saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>Raised after any change, so the bar redraws and the owner can compare with what's saved.</summary>
    public event EventHandler? Changed;

    public void Load(DayTemplate template)
    {
        Blocks = template.Blocks;
        _loading = true;
        Mode = template.Mode;
        UseWorkWindow = template.Mode == DayMode.Schedule && template.WorkWindow is not null;
        WindowStart = template.WorkWindow?.Start.ToTimeSpan() ?? new TimeSpan(9, 0, 0);
        WindowEnd = template.WorkWindow?.End.ToTimeSpan() ?? new TimeSpan(17, 0, 0);
        BudgetHours = (decimal)((template.BudgetMinutes ?? DefaultBudgetMinutes) / 60.0);
        _loading = false;
        Parts.Clear();
        foreach (PlanPart part in template.Parts.OrderBy(PositionInDay))
        {
            Parts.Add(new PlanPartRowViewModel(part, this));
        }

        Selected = null;
        Refresh(rowsChanged: true);
    }

    public void SetCategories(IReadOnlyList<DayCategory> categories)
    {
        Categories = categories;
        foreach (PlanPartRowViewModel row in Parts)
        {
            row.RefreshCategories();
        }

        Refresh(rowsChanged: false);
    }

    /// <summary>What's wrong with the template as it is now (ignoring a message left by a refused click); null = fine.</summary>
    public string? ProblemNow()
    {
        return Parts.Any(row => row.ToPart() is null)
            ? "Pick a category and both times for every part."
            : DayPlanValidator.Validate(ToTemplate(), Categories);
    }

    public DayTemplate ToTemplate()
    {
        WorkWindow? window = Mode == DayMode.Schedule && UseWorkWindow && WindowStart is TimeSpan start && WindowEnd is TimeSpan end
            ? new WorkWindow(TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end))
            : null;
        int? budget = Mode == DayMode.Budget ? (int)Math.Round((double)(BudgetHours ?? DefaultBudgetMinutes / 60m) * 60) : null;
        return new DayTemplate(Parts.Select(row => row.ToPart()).OfType<PlanPart>().ToList(), Blocks, Mode, window, budget);
    }

    partial void OnModeChanged(DayMode value)
    {
        if (_loading)
        {
            return;
        }

        if (value == DayMode.Budget)
        {
            // A Budget day's parts are only non-productive windows: the day's work parts give way to the budget.
            LoadParts(Parts.Select(row => row.ToPart()).OfType<PlanPart>().Where(part => !IsWorkPart(part)).ToList());
        }

        foreach (PlanPartRowViewModel row in Parts)
        {
            row.RefreshCategories();
        }

        Refresh(rowsChanged: false);
    }

    partial void OnUseWorkWindowChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        List<PlanPart> parts = Parts.Select(row => row.ToPart()).OfType<PlanPart>().ToList();
        if (value)
        {
            // The timeline's work parts become the window: the first start to the last end, cut back where a fixed rest
            // or sleep part starts inside it (a window can't overlap them), so the result is a valid day to adjust.
            List<PlanPart> work = parts.Where(IsWorkPart).ToList();
            if (work.Count > 0)
            {
                TimeSpan start = work.Min(part => part.Start.ToTimeSpan());
                TimeSpan end = work.Where(part => !part.CrossesMidnight).Select(part => part.End.ToTimeSpan()).DefaultIfEmpty(new TimeSpan(17, 0, 0)).Max();
                TimeSpan firstBreak = parts
                    .Where(part => !IsWorkPart(part) && !part.IsFlexible && !part.CrossesMidnight && part.Start.ToTimeSpan() > start && part.Start.ToTimeSpan() < end)
                    .Select(part => part.Start.ToTimeSpan())
                    .DefaultIfEmpty(end)
                    .Min();
                WindowStart = start;
                WindowEnd = firstBreak;
            }

            LoadParts(parts.Where(part => !IsWorkPart(part)).ToList());
        }
        else if (WindowStart is TimeSpan start && WindowEnd is TimeSpan end
                 && Categories.FirstOrDefault(category => category.Behavior == CategoryBehavior.Watch) is DayCategory work
                 && !parts.Any(IsWorkPart))
        {
            // Back to the timeline: the window becomes its one work part.
            parts.Add(new PlanPart(Guid.NewGuid(), work.Id, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end)));
            LoadParts(parts);
        }

        Refresh(rowsChanged: false);
    }

    partial void OnWindowStartChanged(TimeSpan? value) => FieldEdited();

    partial void OnWindowEndChanged(TimeSpan? value) => FieldEdited();

    partial void OnBudgetHoursChanged(decimal? value) => FieldEdited();

    private void FieldEdited()
    {
        if (!_loading)
        {
            Refresh(rowsChanged: false);
        }
    }

    private bool IsWorkPart(PlanPart part)
    {
        return Categories.FirstOrDefault(category => category.Id == part.CategoryId)?.Behavior.IsWork() == true;
    }

    // Replaces the rows with these parts, leaving the mode fields as they are.
    private void LoadParts(IReadOnlyList<PlanPart> parts)
    {
        Parts.Clear();
        foreach (PlanPart part in parts.OrderBy(PositionInDay))
        {
            Parts.Add(new PlanPartRowViewModel(part, this));
        }

        Selected = null;
        Refresh(rowsChanged: true);
    }

    /// <summary>Adds an hour-long part (the first Rest category) at the clicked time, shortened to fit, and selects it.</summary>
    public void AddAt(int startMinute)
    {
        DayCategory? category = Categories.FirstOrDefault(candidate => candidate.Behavior == CategoryBehavior.Rest) ?? Categories.FirstOrDefault();
        if (category is null)
        {
            return;
        }

        DayTemplate? next = DayTemplateEditing.AddAt(ToTemplate(), startMinute, category.Id, Categories);
        if (next is null)
        {
            Error = "There's no free time there for a new part.";
            return;
        }

        Guid added = next.Parts[^1].Id;
        Load(next);
        Selected = Parts.First(row => row.Id == added);
    }

    /// <summary>Resizes a part (an edge dragged): cut back where it would run into another.</summary>
    /// <param name="crossesMidnight">Whether the part crossed midnight when the drag started (it stays that way).</param>
    /// <returns>False when the change was refused (less than 15 minutes would be left).</returns>
    public bool Move(Guid partId, int startMinute, int endMinute, bool crossesMidnight)
    {
        DayTemplate? next = DayTemplateEditing.Move(ToTemplate(), partId, startMinute, endMinute, crossesMidnight, Categories);
        return next is not null && Show(next, partId);
    }

    /// <summary>Moves a whole part (its middle dragged), keeping its length; refused where it would hit another part.</summary>
    public bool MoveWhole(Guid partId, int startMinute, int lengthMinutes)
    {
        int start = Math.Clamp(DayTemplateEditing.Snap(startMinute), 0, 24 * 60 - lengthMinutes);
        DayTemplate? next = DayTemplateEditing.Move(ToTemplate(), partId, start, start + lengthMinutes, crossesMidnight: false, Categories);
        PlanPart? moved = next?.Parts.First(part => part.Id == partId);
        if (moved is null || (moved.EndMinutes - (int)moved.Start.ToTimeSpan().TotalMinutes) != lengthMinutes)
        {
            return false;
        }

        return Show(next!, partId);
    }

    public void Delete(Guid partId)
    {
        PlanPartRowViewModel? row = Parts.FirstOrDefault(candidate => candidate.Id == partId);
        if (row is null)
        {
            return;
        }

        Parts.Remove(row);
        if (Selected == row)
        {
            Selected = null;
        }

        Refresh(rowsChanged: true);
    }

    /// <summary>A row's label, category or time was edited in the list.</summary>
    internal void OnRowEdited()
    {
        Refresh(rowsChanged: false);
    }

    partial void OnSelectedChanged(PlanPartRowViewModel? value)
    {
        foreach (PlanPartRowViewModel row in Parts)
        {
            row.IsSelected = row == value;
        }

        ShowRows();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowAll()
    {
        Selected = null;
    }

    private bool Show(DayTemplate template, Guid partId)
    {
        Parts.First(row => row.Id == partId).Show(template.Parts.First(part => part.Id == partId));
        Refresh(rowsChanged: false);
        return true;
    }

    /// <param name="rowsChanged">Rows were added or removed; editing a row in place keeps the list as it is, so its
    /// boxes keep the keyboard.</param>
    private void Refresh(bool rowsChanged)
    {
        Error = ProblemNow();
        if (rowsChanged)
        {
            ShowRows();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ShowRows()
    {
        ShownRows.Clear();
        IEnumerable<PlanPartRowViewModel> rows = Selected is null
            ? Parts.OrderBy(row => row.ToPart() is PlanPart part ? PositionInDay(part) : int.MaxValue)
            : new[] { Selected };
        foreach (PlanPartRowViewModel row in rows)
        {
            ShownRows.Add(row);
        }
    }

    /// <summary>Night parts that started the evening before come first, then by start.</summary>
    private static int PositionInDay(PlanPart part)
    {
        return part.CrossesMidnight ? -1 : (int)part.Start.ToTimeSpan().TotalMinutes;
    }
}
