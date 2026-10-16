using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Friction before ending a focus phase early: a countdown that must run out, then a typed reason.
/// The countdown starts when the dialog opens.
/// </summary>
public sealed partial class EndSessionDialogViewModel : ObservableObject, IDisposable
{
    private readonly BypassPolicy _policy;
    private readonly BypassSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _requestedAt;
    private readonly DispatcherTimer _timer;

    public EndSessionDialogViewModel(BypassSettings settings, TimeProvider timeProvider)
    {
        _settings = settings;
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

    public string ReasonHint => $"Why are you ending this focus session early? (at least {_settings.MinReasonLength} characters)";

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
            BypassCheckResult.WaitNotElapsed => $"Take a breath. You can end the session in {Math.Ceiling(remaining.TotalSeconds):0} s.",
            BypassCheckResult.ReasonTooShort => $"Write at least {_settings.MinReasonLength} characters about why.",
            _ => "You can end the session now.",
        };

        CanConfirm = result == BypassCheckResult.Allowed;
    }
}
