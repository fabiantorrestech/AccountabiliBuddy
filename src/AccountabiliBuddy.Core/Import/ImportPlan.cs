using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Import;

/// <summary>
/// What an import adds: new profiles. It never changes or removes anything that exists. Imported schedules stay with
/// the blocker they came from, which enforces them (ADR 18).
/// </summary>
/// <param name="Notes">Lists left out and entries that couldn't be read.</param>
public sealed record ImportPlan(IReadOnlyList<PlannedProfile> Profiles, IReadOnlyList<string> Notes)
{
    public static ImportPlan Empty { get; } = new(Array.Empty<PlannedProfile>(), Array.Empty<string>());

    public ProfileDocument ApplyTo(ProfileDocument document)
    {
        return document with
        {
            Profiles = document.Profiles.Concat(Profiles.Select(planned => planned.Profile)).ToList(),
        };
    }
}
