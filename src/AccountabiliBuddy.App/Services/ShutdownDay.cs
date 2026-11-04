namespace AccountabiliBuddy.App.Services;

/// <summary>A day the user finished the shutdown routine for (inside the "shutdown" document; kept 90 days).</summary>
/// <param name="Date">The plan date that was wrapped up.</param>
/// <param name="CompletedAt">When "Work over for today" was pressed.</param>
/// <param name="Checked">What was checked, as the closing screen listed it (plain step names, no private text).</param>
/// <param name="Line">The closing line as it was then.</param>
/// <param name="AfterWork">It was finished when the day's work was over (or within the reminder minutes before it ends). A finish in the middle of the
/// workday doesn't count, so the card still comes when work ends; only a finish after work makes "Work over for today" hold.</param>
public sealed record ShutdownDay(DateOnly Date, DateTimeOffset CompletedAt, IReadOnlyList<string> Checked, string Line, bool AfterWork = true);
