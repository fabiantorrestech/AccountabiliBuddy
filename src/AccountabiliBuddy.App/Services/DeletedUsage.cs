using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>One Delete from History: what the user saw and deleted, and the time it covers.</summary>
/// <param name="Label">E.g. "brave" or "reddit.com in brave".</param>
/// <param name="PeriodLabel">E.g. "Today, Monday 19 October".</param>
/// <param name="Parts">The app/site time left out (several when the row added up several names).</param>
public sealed record DeletedUsage(Guid Id, string Label, string PeriodLabel, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<UsageDeletion> Parts);
