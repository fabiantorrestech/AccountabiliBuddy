namespace AccountabiliBuddy.Core.Domain;

/// <summary>
/// A stretch of distracting time inside a focus session you started, from the activity history (readable columns
/// only, ADR 8).
/// </summary>
/// <param name="Domain">The site, when it was a page in a browser.</param>
/// <param name="Section">The site's section (ADR 17), e.g. "r/manybaggers".</param>
public sealed record DriftStretch(DateTimeOffset Start, DateTimeOffset End, string ProcessName, string? Domain, string? Section)
{
    /// <summary>What it was, as shown: the section, else the site, else the app.</summary>
    public string Site => Section ?? Domain ?? ProcessName;
}
