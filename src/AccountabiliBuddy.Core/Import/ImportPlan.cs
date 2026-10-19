using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Import;

/// <summary>What an import adds: new profiles and weekly blocks. It never changes or removes anything that exists.</summary>
/// <param name="Notes">Lists left out, entries that couldn't be read, and schedule parts that clash with existing blocks.</param>
public sealed record ImportPlan(IReadOnlyList<PlannedProfile> Profiles, IReadOnlyList<ScheduleBlock> Blocks, IReadOnlyList<string> Notes)
{
    public static ImportPlan Empty { get; } = new(Array.Empty<PlannedProfile>(), Array.Empty<ScheduleBlock>(), Array.Empty<string>());

    public ProfileDocument ApplyTo(ProfileDocument document)
    {
        return document with
        {
            Profiles = document.Profiles.Concat(Profiles.Select(planned => planned.Profile)).ToList(),
            Schedule = document.Schedule with { Blocks = document.Schedule.Blocks.Concat(Blocks).ToList() },
        };
    }
}
