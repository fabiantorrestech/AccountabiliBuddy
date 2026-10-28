using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One coming one-off day in the Plan page's right column ("Nov 3 · Dentist · Light day"); it opens in the month view.</summary>
public sealed partial class PlanOneOffRowViewModel
{
    private readonly Action<DateOnly> _open;

    public PlanOneOffRowViewModel(DateOnly date, string dateText, string text, Action<DateOnly> open)
    {
        Date = date;
        DateText = dateText;
        Text = text;
        _open = open;
    }

    public DateOnly Date { get; }

    public string DateText { get; }

    public string Text { get; }

    [RelayCommand]
    private void Open() => _open(Date);
}
