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

    /// <summary>The sleep goal, for a Sleep part woken by the alarm (its end is bedtime plus this); 8 hours unless set.</summary>
    public Func<TimeSpan> SleepGoal { get; set; } = () => AccountabiliBuddy.Core.Engine.SleepSchedule.DefaultGoal;

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
        return new DayTemplate(Parts.Select(row => row.ToPart()).OfType<PlanPart>().ToList());
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
