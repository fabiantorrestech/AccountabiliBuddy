using AccountabiliBuddy.Core.Domain;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One one-off day in the Exceptions tab: its date, note and parts, with Edit and Delete.</summary>
public sealed partial class OneOffDayRowViewModel
{
    private readonly Action<OneOffDayRowViewModel> _edit;
    private readonly Action<OneOffDayRowViewModel> _delete;

    /// <param name="today">For greying out days that have passed.</param>
    public OneOffDayRowViewModel(OneOffDay day, DayPlan plan, DateOnly today, Action<OneOffDayRowViewModel> edit, Action<OneOffDayRowViewModel> delete)
    {
        Day = day;
        _edit = edit;
        _delete = delete;
        DateText = day.Date.ToString("ddd d MMM yyyy");
        IsPast = day.Date < today;
        PartsText = day.Template.Parts.Count == 0
            ? "Nothing planned"
            : string.Join(" · ", day.Template.Parts
                .OrderBy(part => part.CrossesMidnight ? -1 : part.Start.ToTimeSpan().TotalMinutes)
                .Select(part => $"{part.Label ?? plan.Category(part.CategoryId)?.Name ?? "?"} {part.Start:HH\\:mm}–{part.End:HH\\:mm}"));
    }

    public OneOffDay Day { get; }

    public string DateText { get; }

    public string PartsText { get; }

    public string? Note => Day.Note;

    public bool HasNote => !string.IsNullOrWhiteSpace(Day.Note);

    public bool IsPast { get; }

    [RelayCommand]
    private void Edit()
    {
        _edit(this);
    }

    [RelayCommand]
    private void Delete()
    {
        _delete(this);
    }
}
