using System.Globalization;
using System.Windows.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A day of the "Snooze until…" month grid: past days are off (faded), today is ringed, the picked day filled.</summary>
public sealed record SnoozeDayCellViewModel(DateOnly Date, bool IsInMonth, bool IsPast, bool IsToday, bool IsSelected, ICommand Pick)
{
    public string Text => Date.Day.ToString(CultureInfo.InvariantCulture);

    public bool IsFaint => IsPast || !IsInMonth;

    public bool CanPick => !IsPast;

    public bool IsRinged => IsToday && !IsSelected;

    public string Name => Date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture);
}
