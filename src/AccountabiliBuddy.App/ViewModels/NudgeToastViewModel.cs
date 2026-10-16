using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Corner toast for the Nudge level and for "minimized" notices. Closes itself after a few seconds.</summary>
public sealed partial class NudgeToastViewModel : ObservableObject
{
    public static TimeSpan DisplayTime { get; } = TimeSpan.FromSeconds(8);

    private readonly Action? _onAction;

    /// <param name="actionLabel">Text of the optional action button, e.g. "Back to work"; null hides it.</param>
    public NudgeToastViewModel(string title, string message, string? actionLabel, Action? onAction)
    {
        Title = title;
        Message = message;
        ActionLabel = actionLabel;
        _onAction = onAction;
    }

    public string Title { get; }

    public string Message { get; }

    public string? ActionLabel { get; }

    public bool HasAction => ActionLabel is not null;

    /// <summary>Raised when the toast should close (button pressed or dismissed).</summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Act()
    {
        _onAction?.Invoke();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Dismiss()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
