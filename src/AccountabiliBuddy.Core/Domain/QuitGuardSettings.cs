namespace AccountabiliBuddy.Core.Domain;

/// <summary>The quit safeguard (ADR 59): when AB can't be quit. Device-local, off by default; each condition is chosen on its own.</summary>
/// <param name="On">The master switch.</param>
/// <param name="AllTheTime">Hardcore: locked at all times. Leaving it needs the spare key and the looser-change wait.</param>
/// <param name="DuringSession">Locked during a focus session the user started, breaks included.</param>
/// <param name="Categories">The day plan categories it locks during; null until the user chooses, which means every work category and Pre-Work.</param>
public sealed record QuitGuardSettings(bool On, bool AllTheTime, bool DuringSession, IReadOnlyList<Guid>? Categories)
{
    public static QuitGuardSettings Default { get; } = new(false, false, false, null);
}
