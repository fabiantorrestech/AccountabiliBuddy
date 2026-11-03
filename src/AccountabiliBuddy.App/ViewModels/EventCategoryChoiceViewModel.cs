using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One row of the calendar event picker: a category, "Don't classify" or "Back to automatic".</summary>
public sealed partial class EventCategoryChoiceViewModel
{
    private readonly Action<EventCategoryChoiceViewModel> _choose;

    /// <param name="categoryId">The category a <see cref="EventChoiceKind.Category"/> row gives; null for the other kinds.</param>
    /// <param name="brush">The category's colour; null for the other kinds.</param>
    /// <param name="isCurrent">This is the choice in force now.</param>
    /// <param name="waitText">"Takes effect at 4:05 PM" when this choice is waiting; null otherwise.</param>
    public EventCategoryChoiceViewModel(
        string text, EventChoiceKind kind, Guid? categoryId, IBrush? brush, bool isCurrent, string? waitText, Action<EventCategoryChoiceViewModel> choose)
    {
        Text = text;
        Kind = kind;
        CategoryId = categoryId;
        Brush = brush;
        IsCurrent = isCurrent;
        WaitText = waitText;
        _choose = choose;
    }

    public string Text { get; }

    public EventChoiceKind Kind { get; }

    public Guid? CategoryId { get; }

    public IBrush? Brush { get; }

    public bool HasBrush => Brush is not null;

    public bool IsCurrent { get; }

    /// <summary>A waiting change to this choice ("Takes effect at 4:05 PM"), null when none waits.</summary>
    public string? WaitText { get; }

    public bool IsWaiting => WaitText is not null;

    [RelayCommand]
    private void Choose() => _choose(this);
}
