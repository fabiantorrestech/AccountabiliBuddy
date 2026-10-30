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


    /// <param name="actionLabel">Text of the optional action button, e.g. "Back to work"; null hides it.</param>
    /// <param name="staysOpen">True: stays until dismissed instead of closing after <see cref="DisplayTime"/>.</param>
    /// <param name="accent">The stripe on the left; the nudge amber when null.</param>
    /// <param name="onLater">
    /// Shows a "Later ▾" button: a plain click passes null (the default wait), its menu 5, 15, 30 or 60 minutes. Null
    /// hides it.
    /// </param>
    public NudgeToastViewModel(
        string title, string message, string? actionLabel, Action? onAction, bool staysOpen = false, IBrush? accent = null,
        Action<int?>? onLater = null, TimeSpan? lifeTime = null)
    {
        LifeTime = lifeTime ?? DisplayTime;
        Title = title;
        Message = message;
        ActionLabel = actionLabel;
        _onAction = onAction;
        _onLater = onLater;
        StaysOpen = staysOpen;
        Accent = accent ?? Services.OverlayBrushes.Get("Drift");
    }

    private readonly Action<int?>? _onLater;

    public bool HasLater => _onLater is not null;

    /// <summary>How long it stays before closing itself (when it does not <see cref="StaysOpen"/>).</summary>
    public TimeSpan LifeTime { get; }

    /// <summary>Raised when the card closed itself because nobody answered (the overlay then keeps a dot).</summary>
    public event EventHandler? Expired;

    public void RaiseExpired()
    {
        Expired?.Invoke(this, EventArgs.Empty);
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

    /// <summary>"Later": <paramref name="minutes"/> from the menu ("5", "15", "30", "60"), or none for the default wait.</summary>
    [RelayCommand]
    private void Later(string? minutes)
    {
        _onLater?.Invoke(int.TryParse(minutes, out int chosen) ? chosen : null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
