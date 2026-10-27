namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A day's fixed lunch period (schema v13, <see cref="PlanPart.AutoStart"/>): the <paramref name="Window"/> it lasts, which is
/// also when it starts by itself.
/// </summary>
public sealed record FixedLunch(string Name, string Color, LocalWindow Window)
{
    /// <summary>How long the lunch lasts: the part's length.</summary>
    public TimeSpan Length => Window.End - Window.Start;
}
