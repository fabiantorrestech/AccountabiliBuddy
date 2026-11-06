using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Friction before ending a focus phase early: a countdown that must run out, then a typed reason.
/// The countdown starts when the dialog opens. With the reason typed, "Confirm" during the wait arms the end (ADR 66): the
/// dialog closes and the session ends by itself when the wait is over.
/// </summary>
public sealed partial class EndSessionDialogViewModel : ObservableObject, IDisposable
{
    private readonly BypassPolicy _policy;
    private readonly BypassSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _requestedAt;
    private readonly DispatcherTimer _timer;

    /// <param name="note">Shown above the reason when the wait is longer than usual; null for none.</param>
    /// <param name="wording">The dialog's words; null for ending a session (<see cref="EndDialogWording.EndSession"/>).</param>
    public EndSessionDialogViewModel(BypassSettings settings, TimeProvider timeProvider, string? note = null, EndDialogWording? wording = null)
    {
        _settings = settings;
        Note = note;
        Wording = wording ?? EndDialogWording.EndSession;
        _policy = new BypassPolicy(settings);
        _timeProvider = timeProvider;
        _requestedAt = timeProvider.GetUtcNow();

        Reason = string.Empty;
        StatusText = string.Empty;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    /// <summary>Raised with true to confirm or false to cancel; the dialog closes in response.</summary>
    public event EventHandler<bool>? CloseRequested;

    [ObservableProperty]
    public partial string Reason { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool CanConfirm { get; set; }

    /// <summary>The reason is long enough and the wait still runs: "Confirm" arms the end instead of waiting here.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ArmCommand))]
    public partial bool CanArm { get; set; }

    /// <summary>True while the wait runs: the arm button shows instead of End session.</summary>
    [ObservableProperty]
    public partial bool IsWaiting { get; set; }

    /// <summary>"Confirm · ends in 0:42".</summary>
    [ObservableProperty]
    public partial string ArmText { get; set; } = string.Empty;

    /// <summary>True when the dialog closed through "Confirm" during the wait: the caller arms the end.</summary>
    public bool Armed { get; private set; }

    /// <summary>When the dialog opened: the wait counts from here, armed or not.</summary>
    public DateTimeOffset RequestedAt => _requestedAt;

    public TimeSpan Wait => _settings.WaitDuration;

    public string? Note { get; }

    public bool HasNote => Note is not null;

    public EndDialogWording Wording { get; }

    public string ReasonHint => Wording.ReasonQuestion.Replace("{min}", _settings.MinReasonLength.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand(CanExecute = nameof(CanArm))]
    private void Arm()
    {
        Armed = true;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(this, false);
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    partial void OnReasonChanged(string value)
    {
        Refresh();
    }

    private void Refresh()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        BypassCheckResult result = _policy.Check(_requestedAt, now, Reason);
        TimeSpan remaining = _policy.RemainingWait(_requestedAt, now);

        string left = FormatRemaining(remaining);
        StatusText = result switch
        {
            BypassCheckResult.WaitNotElapsed => Wording.Waiting.Replace("{time}", left),
            BypassCheckResult.ReasonTooShort => $"Write at least {_settings.MinReasonLength} characters about why.",
            _ => Wording.Ready,
        };

        CanConfirm = result == BypassCheckResult.Allowed;
        IsWaiting = remaining > TimeSpan.Zero;
        CanArm = IsWaiting && Reason.Trim().Length >= _settings.MinReasonLength;
        ArmText = Wording.ArmButton.Replace("{time}", left);
        if (IsWaiting && CanArm)
        {
            StatusText = Wording.Armable.Replace("{time}", left);
        }
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return seconds < 60 ? $"{seconds} s" : $"{seconds / 60}:{seconds % 60:00}";
    }
}
