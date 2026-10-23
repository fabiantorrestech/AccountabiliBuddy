using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Something on a row that isn't a session or a plan part: a calendar event or a drift mark.</summary>
/// <param name="Color">"#RRGGBB" (an event's calendar); null = the row's own colour.</param>
/// <param name="AllDay">An all-day event: drawn as a thin sliver along the top of its row, not a full block.</param>
public sealed record TimelineItem(LocalWindow Window, string Tip, string? Color, bool AllDay = false);
