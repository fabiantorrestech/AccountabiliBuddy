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
        Agenda = DayAgenda.For(plan, day.Date);
        IsSameAsDefault = !DayAgenda.DiffersFromDefault(plan, day);
    }

    public OneOffDay Day { get; }

    public string DateText { get; }

    /// <summary>The day as it runs, one part per row (see <see cref="DayAgenda"/>).</summary>
    public IReadOnlyList<AgendaRow> Agenda { get; }

    public bool HasNothingPlanned => Agenda.Count == 0;

    /// <summary>Its parts are the default plan's (saved before such days stopped being kept).</summary>
    public bool IsSameAsDefault { get; }

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
