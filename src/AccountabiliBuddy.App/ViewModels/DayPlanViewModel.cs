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
/// Day plan tab (design part B, ADR 18): the categories, which days are workdays, and the Workdays and Other days
/// templates, each edited with a drag bar and a list. Saved at once (the plan never waits), locked during a focus
/// session the user started. One-off days are edited in the Exceptions tab.
/// </summary>
public sealed partial class DayPlanViewModel : ObservableObject
{
    private static readonly DayOfWeek[] WeekOrder =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    };

    private readonly ConfigurationService _configuration;
    private readonly SessionController _sessions;

    // The version of the saved plan the tab last showed; a different one arriving means it changed elsewhere.
    private string? _shownVersion;

    public DayPlanViewModel(ConfigurationService configuration, SessionController sessions)
    {
        _configuration = configuration;
        _sessions = sessions;
        DayPlan plan = configuration.Current.DayPlan;
        Workday = new DayPlanEditorViewModel(plan.Workday, plan.Categories);
        OtherDay = new DayPlanEditorViewModel(plan.OtherDay, plan.Categories);
        Workday.Changed += (_, _) => RefreshUnsaved();
        OtherDay.Changed += (_, _) => RefreshUnsaved();
        Load(plan);

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

    public DayPlanEditorViewModel Workday { get; }

    public DayPlanEditorViewModel OtherDay { get; }

    /// <summary>Monday first; on = the day follows the Workdays template.</summary>
    public ObservableCollection<WorkdayChip> Days { get; } = new();

    public ObservableCollection<CategoryRowViewModel> Categories { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand), nameof(AddCategoryCommand))]
    public partial bool IsLocked { get; set; }

    /// <summary>The inner tab showing: 0 = Workdays, 1 = Other days (search can switch it).</summary>
    [ObservableProperty]
    public partial int SelectedTemplateIndex { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    private bool CanEdit => !IsLocked;

    private bool CanSave => !IsLocked && HasUnsavedChanges;

    public DayPlan Build()
    {
        return _configuration.Current.DayPlan with
        {
            Categories = Categories.Select(row => row.ToCategory()).ToList(),
            Workdays = Days.Where(chip => chip.IsOn).Select(chip => chip.Day).ToList(),
            Workday = Workday.ToTemplate(),
            OtherDay = OtherDay.ToTemplate(),
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

    private void DeleteCategory(CategoryRowViewModel row)
    {
        if (IsLocked)
        {
            return;
        }

        DayPlan plan = _configuration.Current.DayPlan;
        bool used = Workday.ToTemplate().Parts.Concat(OtherDay.ToTemplate().Parts)
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
        foreach (CategoryRowViewModel row in Categories)
        {
            row.PropertyChanged -= OnCategoryRowChanged;
        }

        Categories.Clear();
        foreach (DayCategory category in plan.Categories)
        {
            AddCategoryRow(category);
        }

        foreach (WorkdayChip chip in Days)
        {
            chip.PropertyChanged -= OnChipChanged;
        }

        Days.Clear();
        foreach (DayOfWeek day in WeekOrder)
        {
            var chip = new WorkdayChip(day, plan.Workdays.Contains(day));
            chip.PropertyChanged += OnChipChanged;
            Days.Add(chip);
        }

        Workday.SetCategories(plan.Categories);
        OtherDay.SetCategories(plan.Categories);
        Workday.Load(plan.Workday);
        OtherDay.Load(plan.OtherDay);
        RefreshUnsaved();
    }

    private void AddCategoryRow(DayCategory category)
    {
        var row = new CategoryRowViewModel(category, DeleteCategory);
        row.PropertyChanged += OnCategoryRowChanged;
        Categories.Add(row);
    }

    private void OnCategoryRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        CategoriesEdited();
    }

    private void OnChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshUnsaved();
    }

    private void CategoriesEdited()
    {
        IReadOnlyList<DayCategory> categories = Categories.Select(row => row.ToCategory()).ToList();
        Workday.SetCategories(categories);
        OtherDay.SetCategories(categories);
        RefreshUnsaved();
    }

    // Records holding lists compare lists by reference, so the plans are compared as JSON, in a fixed order (the
    // editor lists parts in the order of the day, the saved plan in the order they were saved).
    private void RefreshUnsaved()
    {
        HasUnsavedChanges = JsonSerializer.Serialize(Normalized(Build())) != JsonSerializer.Serialize(Normalized(_configuration.Current.DayPlan));
    }

    private static DayPlan Normalized(DayPlan plan)
    {
        return plan with
        {
            Workdays = plan.Workdays.Order().ToList(),
            Workday = new DayTemplate(plan.Workday.Parts.OrderBy(part => part.Id).ToList()),
            OtherDay = new DayTemplate(plan.OtherDay.Parts.OrderBy(part => part.Id).ToList()),
        };
    }
}
