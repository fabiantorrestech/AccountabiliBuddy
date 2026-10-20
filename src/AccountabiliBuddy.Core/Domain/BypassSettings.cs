namespace AccountabiliBuddy.Core.Domain;

/// <summary>Friction applied before a session can be ended early or the app quit.</summary>
public sealed record BypassSettings(TimeSpan WaitDuration, int MinReasonLength)
{
    public static BypassSettings Default { get; } = new(
        WaitDuration: TimeSpan.FromSeconds(60),
        MinReasonLength: 15);

    /// <summary>No wait and no reason: for sessions that end on command (see <see cref="Engine.BypassPolicy.EndsOnCommand"/>).</summary>
    public static BypassSettings None { get; } = new(WaitDuration: TimeSpan.Zero, MinReasonLength: 0);
}
