using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Stricter changes take effect at once; looser ones wait (<see cref="LockSettings.LooseningDelay"/>).
/// Splits a proposed configuration by section (each profile, the settings): a section that only got stricter or
/// stayed neutral is applied now, a section with any looser change keeps its current value until the wait is over
/// (a profile's new name still applies now).
/// The day plan always applies at once (ADR 18). See <see cref="LooseningDetector"/> for what counts as looser.
/// Deleting a profile here takes effect at once; a profile deleted on another device waits like a looser change
/// (ADR 22), so a received change never takes away a profile at once, least of all the one a session runs.
/// </summary>
public static class ConfigurationChangeGuard
{
    /// <param name="deletionsThatWait">Profiles (by id) whose deletion waits: deleted on another device. Any other
    /// profile missing from <paramref name="proposed"/> is deleted at once.</param>
    public static ConfigurationSplit Split(ProfileDocument current, ProfileDocument proposed, IReadOnlySet<Guid>? deletionsThatWait = null)
    {
        var waiting = new List<string>();

        IReadOnlyList<string> settingsChanges = LooseningDetector.Settings(current, proposed);
        waiting.AddRange(settingsChanges);

        var profiles = new List<FocusProfile>();
        foreach (FocusProfile after in proposed.Profiles)
        {
            FocusProfile? before = current.Profiles.FirstOrDefault(profile => profile.Id == after.Id);
            IReadOnlyList<string> changes = before is null
                ? Array.Empty<string>()
                : LooseningDetector.Profile(current, before, after);

            waiting.AddRange(changes);
            // A profile with looser changes keeps its current value, except its name: only a label, so a rename never waits.
            profiles.Add(changes.Count > 0 ? before! with { Name = after.Name } : after);
        }

        foreach (FocusProfile deleted in Deleted(current, proposed))
        {
            if (deletionsThatWait is not null && deletionsThatWait.Contains(deleted.Id))
            {
                waiting.Add(DescribeDeletedElsewhere(deleted));
                profiles.Add(deleted);
            }
        }

        ProfileDocument applyNow = proposed with
        {
            Profiles = profiles,
            PomodoroDefaults = settingsChanges.Count > 0 ? current.PomodoroDefaults : proposed.PomodoroDefaults,
            Escalation = settingsChanges.Count > 0 ? current.Escalation : proposed.Escalation,
            Bypass = settingsChanges.Count > 0 ? current.Bypass : proposed.Bypass,
            Locks = settingsChanges.Count > 0 ? current.Locks : proposed.Locks,
        };

        return new ConfigurationSplit(applyNow, waiting);
    }

    /// <summary>
    /// The deletions still waiting between the configuration in effect and a pending one: every profile
    /// <paramref name="current"/> has and <paramref name="pending"/> doesn't (a deletion made here never waits).
    /// </summary>
    /// <returns>One line per profile, as <see cref="Split"/> describes it.</returns>
    public static IReadOnlyList<string> DeletionsWaiting(ProfileDocument current, ProfileDocument pending)
    {
        return Deleted(current, pending).Select(DescribeDeletedElsewhere).ToList();
    }

    private static IEnumerable<FocusProfile> Deleted(ProfileDocument from, ProfileDocument to)
    {
        return from.Profiles.Where(profile => to.Profiles.All(kept => kept.Id != profile.Id));
    }

    private static string DescribeDeletedElsewhere(FocusProfile profile)
    {
        return $"{profile.Name}: deleted on another device";
    }
}
