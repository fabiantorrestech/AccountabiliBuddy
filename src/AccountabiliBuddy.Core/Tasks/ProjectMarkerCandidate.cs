namespace AccountabiliBuddy.Core.Tasks;

/// <summary>A marker style the user's titles keep using, worth asking about once ("Treat ⟦…⟧ as a project marker?").</summary>
/// <param name="Count">How many titles in the last 30 days start that way.</param>
public sealed record ProjectMarkerCandidate(ProjectMarkerPattern Pattern, int Count);
