using AccountabiliBuddy.Core.Domain;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One part in the day plan editor's list: its label, category and times, kept in step with the bar.</summary>
public sealed partial class PlanPartRowViewModel : ObservableObject
{
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#64748B"));

    private readonly DayPlanEditorViewModel _owner;

    // True while showing a part from the editor, so the change isn't reported back as an edit.
    private bool _showing;

    public PlanPartRowViewModel(PlanPart part, DayPlanEditorViewModel owner)
    {
        _owner = owner;
        Id = part.Id;
        Label = string.Empty;
        Show(part);
    }

    public Guid Id { get; }

    public IReadOnlyList<DayCategory> Categories => _owner.Categories;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(ColorBrush), nameof(Color), nameof(IsSleep), nameof(EndEditable))]
    public partial DayCategory? Category { get; set; }

    /// <summary>Shown instead of the category's name (e.g. "After-office work"); empty = the category's name.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    public partial string Label { get; set; }

    [ObservableProperty]
    public partial TimeSpan? Start { get; set; }

    [ObservableProperty]
    public partial TimeSpan? End { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>A flexible break (schema v8): the times are its window; it lasts <see cref="FlexMinutes"/> once started.</summary>
    [ObservableProperty]
    public partial bool IsFlexible { get; set; }

    [ObservableProperty]
    public partial decimal? FlexMinutes { get; set; }

    /// <summary>A hint when not started by then; null = none.</summary>
    [ObservableProperty]
    public partial TimeSpan? HintAt { get; set; }

    /// <summary>
    /// A Sleep part woken by the alarm (schema v9, ADR 33): only the bedtime is set; the end follows bedtime plus the
    /// sleep goal and the morning's alarm sets the real wake time.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndEditable))]
    public partial bool WakeByAlarm { get; set; }

    /// <summary>A Sleep part: it can wake by the alarm.</summary>
    public bool IsSleep => Category?.Behavior == CategoryBehavior.Sleep;

    /// <summary>The end is set by hand (not a Sleep part woken by the alarm).</summary>
    public bool EndEditable => !(IsSleep && WakeByAlarm);

    public string Name => string.IsNullOrWhiteSpace(Label) ? Category?.Name ?? "?" : Label.Trim();

    /// <summary>"#RRGGBB", for drawing on the bar.</summary>
    public string Color => Category?.Color ?? "#64748B";

    public IBrush ColorBrush => Category is null ? UnknownBrush : new SolidColorBrush(Avalonia.Media.Color.Parse(Category.Color));

    /// <returns>The part, or null while its category or a time isn't picked.</returns>
    public PlanPart? ToPart()
    {
        if (Category is null || Start is not TimeSpan start || End is not TimeSpan end)
        {
            return null;
        }

        return new PlanPart(Id, Category.Id, TimeOnly.FromTimeSpan(start), TimeOnly.FromTimeSpan(end),
            string.IsNullOrWhiteSpace(Label) ? null : Label.Trim(),
            IsFlexible ? (int)Math.Clamp(FlexMinutes ?? 60, 15, 240) : null,
            IsFlexible && HintAt is TimeSpan hint ? TimeOnly.FromTimeSpan(hint) : null,
            IsSleep && WakeByAlarm);
    }

    internal void Show(PlanPart part)
    {
        _showing = true;
        Category = _owner.Categories.FirstOrDefault(category => category.Id == part.CategoryId);
        Label = part.Label ?? string.Empty;
        Start = part.Start.ToTimeSpan();
        End = part.End.ToTimeSpan();
        IsFlexible = part.IsFlexible;
        FlexMinutes = part.FlexMinutes ?? 60;
        HintAt = part.HintAt?.ToTimeSpan() ?? (part.IsFlexible ? null : new TimeSpan(13, 0, 0));
        WakeByAlarm = part.WakeByAlarm;
        _showing = false;
    }

    /// <summary>After categories were renamed, recoloured, added or removed: the same category by id.</summary>
    internal void RefreshCategories()
    {
        _showing = true;
        Guid? id = Category?.Id;
        OnPropertyChanged(nameof(Categories));
        Category = _owner.Categories.FirstOrDefault(category => category.Id == id);
        _showing = false;
    }

    partial void OnCategoryChanged(DayCategory? value)
    {
        Edited();
    }

    partial void OnLabelChanged(string value)
    {
        Edited();
    }

    partial void OnStartChanged(TimeSpan? value)
    {
        FollowGoal();
        Edited();
    }

    partial void OnEndChanged(TimeSpan? value)
    {
        Edited();
    }

    partial void OnIsFlexibleChanged(bool value)
    {
        Edited();
    }

    partial void OnFlexMinutesChanged(decimal? value)
    {
        Edited();
    }

    partial void OnHintAtChanged(TimeSpan? value)
    {
        Edited();
    }

    partial void OnWakeByAlarmChanged(bool value)
    {
        FollowGoal();
        Edited();
    }

    // Woken by the alarm: the end is bedtime plus the sleep goal (what the night is when no alarm is set).
    private void FollowGoal()
    {
        if (_showing || !IsSleep || !WakeByAlarm || Start is not TimeSpan start)
        {
            return;
        }

        TimeSpan end = start + _owner.SleepGoal();
        End = TimeSpan.FromMinutes(Math.Round(end.TotalMinutes) % (24 * 60));
    }

    private void Edited()
    {
        if (!_showing)
        {
            _owner.OnRowEdited();
        }
    }

    [RelayCommand]
    private void Delete()
    {
        _owner.Delete(Id);
    }
}
