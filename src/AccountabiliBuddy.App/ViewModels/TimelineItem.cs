using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>Something on a row that isn't a session or a plan part: a calendar event or a drift mark.</summary>
/// <param name="Color">"#RRGGBB" (an event's calendar); null = the row's own colour.</param>
public sealed record TimelineItem(LocalWindow Window, string Tip, string? Color);
