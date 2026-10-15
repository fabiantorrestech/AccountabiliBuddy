namespace AccountabiliBuddy.Core.Ports;

/// <summary>Shows a toast notification (the Nudge escalation level).</summary>
public interface INotifier
{
    Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default);
}
