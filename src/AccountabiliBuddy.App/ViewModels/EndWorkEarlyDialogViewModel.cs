using System.Globalization;
using AccountabiliBuddy.App.Services;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// "End work early?" (ADR 65), before Start shutdown opens the routine during a work part: the same wait and reason as ending a
/// session early (<see cref="BypassPolicy"/>), and, when more work comes later the same day, "Also end later work today" (off by
/// default). The countdown starts when the dialog opens. With the reason typed, "Confirm" during the wait arms the end (ADR 66):
/// the dialog closes and work ends by itself when the wait is over.
/// </summary>
public sealed partial class EndWorkEarlyDialogViewModel : ObservableObject, IDisposable
{
    private readonly BypassPolicy _policy;
    private readonly BypassSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _requestedAt;
    private readonly DispatcherTimer _timer;

    public EndWorkEarlyDialogViewModel(WorkEndProposal proposal, BypassSettings settings, TimeProvider timeProvider)
    {
        _settings = settings;
        _policy = new BypassPolicy(settings);
        _timeProvider = timeProvider;
        _requestedAt = timeProvider.GetUtcNow();
        HasLater = proposal.HasLater;
        Summary = $"This work block was planned until {Clock(proposal.Current.End)}. Ending it now quiets the start nudges for the rest of it, "
            + "and the shutdown routine opens. \"Back to work\" on Today undoes it while the block is still planned.";
        LaterText = proposal.HasLater
            ? $"Also end later work today ({string.Join(", ", proposal.Later.Select(window => $"{Clock(window.Start)} – {Clock(window.End)}"))})"
            : string.Empty;

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

    /// <summary>True while the wait runs: the arm button shows instead of End work.</summary>
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

    /// <summary>"Also end later work today": off by default, so only the current block ends.</summary>
    [ObservableProperty]
    public partial bool AlsoEndLater { get; set; }

    public string Summary { get; }

    public bool HasLater { get; }

    public string LaterText { get; }

    public string ReasonHint => $"Why are you ending work early? (at least {_settings.MinReasonLength} characters)";

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

        StatusText = result switch
        {
            BypassCheckResult.WaitNotElapsed => $"Take a breath. You can end work in {FormatRemaining(remaining)}.",
            BypassCheckResult.ReasonTooShort => $"Write at least {_settings.MinReasonLength} characters about why.",
            _ => "You can end work now.",
        };

        CanConfirm = result == BypassCheckResult.Allowed;
        IsWaiting = remaining > TimeSpan.Zero;
        CanArm = IsWaiting && Reason.Trim().Length >= _settings.MinReasonLength;
        ArmText = $"Confirm · ends in {FormatRemaining(remaining)}";
        if (IsWaiting && CanArm)
        {
            StatusText = $"Confirm to keep going: work ends by itself in {FormatRemaining(remaining)}, and you can cancel until then.";
        }
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return seconds < 60 ? $"{seconds} s" : $"{seconds / 60}:{seconds % 60:00}";
    }

    private static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
