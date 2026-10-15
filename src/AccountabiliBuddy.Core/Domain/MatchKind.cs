namespace AccountabiliBuddy.Core.Domain;

/// <summary>How a rule's pattern is compared. All comparisons are case-insensitive.</summary>
public enum MatchKind
{
    Exact,
    Contains,
    Prefix,
    Regex,
    /// <summary>URL rules only: matches the host and any of its subdomains.</summary>
    Domain,
}
