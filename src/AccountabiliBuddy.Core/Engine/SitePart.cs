using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>A part of a site as a rule, e.g. Url Prefix "reddit.com/r/dotnet", with how the classify prompt names it.</summary>
public sealed record SitePart(MatchTarget Target, MatchKind Kind, string Pattern, string Label);
