namespace AccountabiliBuddy.Core.Domain;

/// <summary>A site or app called on track in enough sessions to be offered as a permanent exemption.</summary>
/// <param name="Key">The site, or the app without one (lowercase); also what "review.promoted" keeps.</param>
public sealed record RepeatAllow(string Key, string Display, Rule SuggestedRule);
