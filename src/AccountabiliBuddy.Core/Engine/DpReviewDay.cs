namespace AccountabiliBuddy.Core.Engine;

/// <summary>One kind of day in the Setup DP Assistant's review: a heading ("Workday · Mon–Fri") and what the buddy does through it.</summary>
public sealed record DpReviewDay(string Title, IReadOnlyList<string> Lines);
