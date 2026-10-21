namespace AccountabiliBuddy.Core.Link;

/// <summary>A part of the day plan on a day, cut to that day.</summary>
/// <param name="Color">The category's colour, "#RRGGBB".</param>
/// <param name="Kind">"watch" (planned work), "rest" or "sleep".</param>
public sealed record LinkPlanPart(string Name, string Category, string Color, string Kind, DateTimeOffset Start, DateTimeOffset End);
