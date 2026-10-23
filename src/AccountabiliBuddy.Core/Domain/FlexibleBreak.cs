namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A day's flexible break (schema v8, e.g. lunch): the <paramref name="Window"/> it may start in, how long it lasts,
/// and when to hint if it hasn't started (null = no hint).
/// </summary>
public sealed record FlexibleBreak(string Name, string Color, LocalWindow Window, TimeSpan Length, DateTime? HintAt);
