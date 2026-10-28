using System.Windows.Input;
using AccountabiliBuddy.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "Add to Today" at three tasks (ADR 25): the card that asks before anything is added. The task is never added silently and
/// never dropped: it is added anyway with a reason (10+ characters, kept on this PC and counted for the review, like "Add
/// anyway" on Today), swapped for one of today's open tasks (that one is parked as a come-back), or left where it is.
/// </summary>
public sealed partial class PendingMoveViewModel : ObservableObject
{
    private readonly Func<string, Task<AddToTodayResult>> _addAnyway;
    private readonly Func<Guid, Task> _swap;
    private readonly Action _close;

    public PendingMoveViewModel(
        string title, bool canSwap, IReadOnlyList<(Guid Id, string Title)> swapChoices,
        Func<string, Task<AddToTodayResult>> addAnyway, Func<Guid, Task> swap, Action close)
    {
        TitleText = title;
        _addAnyway = addAnyway;
        _swap = swap;
        _close = close;
        CanSwap = canSwap && swapChoices.Count > 0;
        SwapChoices = swapChoices.Select(choice => new SwapChoice(choice.Title, new AsyncRelayCommand(() => swap(choice.Id)))).ToList();
        Reason = string.Empty;
    }

    /// <summary>E.g. "Today already has 3 tasks. Add “Draft Q4 plan” anyway?"</summary>
    public string TitleText { get; }

    /// <summary>Today's open tasks it can take the place of.</summary>
    public IReadOnlyList<SwapChoice> SwapChoices { get; }

    public bool CanSwap { get; }

    [ObservableProperty]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private async Task AddAnywayAsync()
    {
        AddToTodayResult result = await _addAnyway(Reason);
        if (result == AddToTodayResult.ReasonTooShort)
        {
            Error = $"Write why, at least {SessionTasksService.MinOverLimitReasonLength} characters.";
        }
        else
        {
            _close();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _close();
    }

    /// <summary>One of today's open tasks the new one can take the place of.</summary>
    public sealed record SwapChoice(string Title, ICommand SwapCommand);
}
