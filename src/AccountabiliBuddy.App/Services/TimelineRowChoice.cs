namespace AccountabiliBuddy.App.Services;

/// <summary>One row of the timeline as set in Settings: where it is (its place in the list) and whether it shows.</summary>
public sealed record TimelineRowChoice(TimelineRow Row, bool Shown);
