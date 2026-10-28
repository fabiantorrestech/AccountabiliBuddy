using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One day type in the Plan page's right column: its name, the weekdays that follow it, a chevron that opens its editor.</summary>
public sealed partial class PlanDayTypeRowViewModel
{
    private readonly Action<Guid> _open;

    public PlanDayTypeRowViewModel(Guid id, string name, string daysText, bool isSelected, Action<Guid> open)
    {
        Id = id;
        Name = name;
        DaysText = daysText;
        IsSelected = isSelected;
        _open = open;
    }

    public Guid Id { get; }

    public string Name { get; }

    /// <summary>"Mon, Tue, Wed, Fri", or "No weekday" when none follows it.</summary>
    public string DaysText { get; }

    /// <summary>The selected day follows this type (a Surface2 row).</summary>
    public bool IsSelected { get; }

    [RelayCommand]
    private void Open() => _open(Id);
}
