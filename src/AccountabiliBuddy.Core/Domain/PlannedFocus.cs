namespace AccountabiliBuddy.Core.Domain;

/// <summary>Time the day plan watches for drift: one Watch part (Work, Extra Work…) on a real date.</summary>
/// <param name="Name">The part's label or its category's name.</param>
/// <param name="Color">The category's colour, "#RRGGBB".</param>
public sealed record PlannedFocus(string Name, string Color, LocalWindow Window);
