using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Corner toast for the Nudge level and for "minimized" notices. Closes itself after a few seconds, unless it is a
/// reminder that stays until dismissed (e.g. bedtime).
/// </summary>
public sealed partial class NudgeToastViewModel : ObservableObject
{
    public static TimeSpan DisplayTime { get; } = TimeSpan.FromSeconds(8);

    private readonly Action? _onAction;

    private static readonly IBrush NudgeAccent = new SolidColorBrush(Color.Parse("#E0A030"));

    /// <param name="actionLabel">Text of the optional action button, e.g. "Back to work"; null hides it.</param>
    /// <param name="staysOpen">True: stays until dismissed instead of closing after <see cref="DisplayTime"/>.</param>
    /// <param name="accent">The stripe on the left; the nudge amber when null.</param>
    public NudgeToastViewModel(string title, string message, string? actionLabel, Action? onAction, bool staysOpen = false, IBrush? accent = null)
    {
        Title = title;
        Message = message;
        ActionLabel = actionLabel;
        _onAction = onAction;
        StaysOpen = staysOpen;
        Accent = accent ?? NudgeAccent;
    }

    public bool StaysOpen { get; }

    public IBrush Accent { get; }

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
