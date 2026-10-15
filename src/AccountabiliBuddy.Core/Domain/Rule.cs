namespace AccountabiliBuddy.Core.Domain;

/// <summary>One allow or block rule inside a <see cref="FocusProfile"/>.</summary>
public sealed record Rule(
    Guid Id,
    MatchTarget Target,
    MatchKind Kind,
    string Pattern,
    RuleVerdict Verdict,
    string? Note = null);
