namespace AccountabiliBuddy.App.Controls;

/// <summary>One stretch on the day bar, as fractions of its width (0 = left edge, 1 = right edge).</summary>
/// <param name="Tip">Shown when the pointer is over it.</param>
/// <param name="ProfileId">The focus profile of a session; clicking it opens that profile.</param>
/// <param name="Color">"#RRGGBB" (a plan part's category, an event's calendar); null = the row's own colour.</param>
/// <param name="Label">Text drawn inside it when there's room (a plan part's name).</param>
/// <param name="Hatched">Drawn lighter with stripes: a focus session added by hand (ADR 20).</param>
public sealed record DayBarSpan(
    double Start, double End, string Tip, Guid? ProfileId = null, string? Color = null, string? Label = null, bool Hatched = false);
