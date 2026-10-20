namespace AccountabiliBuddy.App.Controls;

/// <summary>One stretch on the day bar, as fractions of its width (0 = left edge, 1 = right edge).</summary>
/// <param name="Tip">Shown when the pointer is over it.</param>
/// <param name="ProfileId">The focus profile of a planned block or a session; clicking it opens that profile.</param>
public sealed record DayBarSpan(double Start, double End, string Tip, Guid? ProfileId = null);
