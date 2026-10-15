namespace AccountabiliBuddy.Core.Domain;

/// <summary>Friction applied before a session can be ended early or the app quit.</summary>
public sealed record BypassSettings(TimeSpan WaitDuration, int MinReasonLength)
{
    public static BypassSettings Default { get; } = new(
        WaitDuration: TimeSpan.FromSeconds(60),
        MinReasonLength: 15);
}
