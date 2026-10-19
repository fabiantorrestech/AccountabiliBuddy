using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Import;

/// <summary>A profile an import will create.</summary>
/// <param name="SourceNames">The imported lists it holds; more than one for a profile made where schedules overlap.</param>
/// <param name="SkippedLines">Entries that couldn't be turned into rules.</param>
public sealed record PlannedProfile(FocusProfile Profile, IReadOnlyList<string> SourceNames, IReadOnlyList<string> SkippedLines)
{
    public bool IsCombined => SourceNames.Count > 1;
}
