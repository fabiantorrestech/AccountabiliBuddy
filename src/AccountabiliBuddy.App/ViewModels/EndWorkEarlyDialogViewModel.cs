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
/// default). The countdown starts when the dialog opens.
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
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        return seconds < 60 ? $"{seconds} s" : $"{seconds / 60}:{seconds % 60:00}";
    }

    private static string Clock(DateTime time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);
}
