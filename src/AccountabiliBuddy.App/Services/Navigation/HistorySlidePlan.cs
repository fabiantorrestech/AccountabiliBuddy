namespace AccountabiliBuddy.App.Services.Navigation;

/// <summary>
/// The page slide of a back / forward move (owner 2026-11-14), as numbers (pure, so it can be checked). Going forward the old page
/// fades out toward the left and the new one fades in from the right; going back, the mirror image. Out: 140 ms ease-in; in: 220 ms
/// ease-out; both at the Animations speed (Settings › Animations).
/// </summary>
public static class HistorySlidePlan
{
    /// <summary>How far a page travels while it fades (DIP).</summary>
    public const double Offset = 48;

    public static readonly TimeSpan OutDuration = TimeSpan.FromMilliseconds(140);

    public static readonly TimeSpan InDuration = TimeSpan.FromMilliseconds(220);

    /// <summary>Where the outgoing page ends (x): left going forward, right going back.</summary>
    public static double OutTarget(HistoryDirection direction) => direction == HistoryDirection.Forward ? -Offset : Offset;

    /// <summary>Where the incoming page starts (x): the side opposite to where the outgoing page went.</summary>
    public static double InStart(HistoryDirection direction) => -OutTarget(direction);

    /// <summary>The outgoing page at <paramref name="t"/> (0 to 1 of its duration): opacity and x, ease-in (cubic).</summary>
    public static (double Opacity, double X) Out(HistoryDirection direction, double t)
    {
        double eased = Math.Pow(Math.Clamp(t, 0, 1), 3);
        return (1 - eased, OutTarget(direction) * eased);
    }

    /// <summary>The incoming page at <paramref name="t"/>: opacity and x, ease-out (cubic).</summary>
    public static (double Opacity, double X) In(HistoryDirection direction, double t)
    {
        double eased = 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
        return (eased, InStart(direction) * (1 - eased));
    }
}
