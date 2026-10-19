namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A trusted time paired with the boot's elapsed-time counter, saved so a restart in the same boot can tell whether
/// the Windows clock was changed while the app was closed.
/// </summary>
/// <param name="BootId">Identifies the boot the counter belongs to; the counter restarts with each boot.</param>
/// <param name="TrustedUtc">The trusted time when the anchor was taken.</param>
/// <param name="ElapsedMilliseconds">The boot's elapsed-time counter (sleep included) at that moment.</param>
public sealed record ClockAnchor(string BootId, DateTimeOffset TrustedUtc, long ElapsedMilliseconds);
