namespace AccountabiliBuddy.Core.Domain;

/// <summary>A kind of time in the day plan, e.g. Work, Extra Work, Lunch or Sleep; the user can add their own.</summary>
/// <param name="Color">"#RRGGBB".</param>
public sealed record DayCategory(Guid Id, string Name, string Color, CategoryBehavior Behavior);
