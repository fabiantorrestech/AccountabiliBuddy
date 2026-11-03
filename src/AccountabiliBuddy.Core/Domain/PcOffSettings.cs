namespace AccountabiliBuddy.Core.Domain;

/// <summary>What happens to a session when the PC goes off during it (ADR 60). Device-local.</summary>
/// <param name="KeepOriginalEnd">The planned end stays put; by default the timer pauses for as long as the PC was off.</param>
/// <param name="TellMe">A notice says what happened when the PC comes back. On by default.</param>
public sealed record PcOffSettings(bool KeepOriginalEnd = false, bool TellMe = true)
{
    public static PcOffSettings Default { get; } = new();
}
