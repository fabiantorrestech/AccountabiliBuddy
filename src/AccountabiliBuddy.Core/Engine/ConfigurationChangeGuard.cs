using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Stricter changes take effect at once; looser ones wait (<see cref="LockSettings.LooseningDelay"/>).
/// Splits a proposed configuration by section (the schedule, each profile, the settings): a section that only got
/// stricter or stayed neutral is applied now, a section with any looser change keeps its current value until the
/// wait is over. See <see cref="LooseningDetector"/> for what counts as looser.
/// </summary>
public static class ConfigurationChangeGuard
{
    public static ConfigurationSplit Split(ProfileDocument current, ProfileDocument proposed)
    {
        var waiting = new List<string>();

        IReadOnlyList<string> scheduleChanges = LooseningDetector.Schedule(current, proposed);
        waiting.AddRange(scheduleChanges);

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
            profiles.Add(changes.Count > 0 ? before! : after);
        }

        // A deleted profile the schedule still uses stays until the wait is over.
        foreach (FocusProfile before in current.Profiles.Where(profile => proposed.Profiles.All(other => other.Id != profile.Id)))
        {
            IReadOnlyList<string> changes = LooseningDetector.Profile(current, before, after: null);
            if (changes.Count > 0)
            {
                waiting.AddRange(changes);
                profiles.Add(before);
            }
        }

        ProfileDocument applyNow = proposed with
        {
            Profiles = profiles,
            Schedule = scheduleChanges.Count > 0 ? current.Schedule : proposed.Schedule,
            PomodoroDefaults = settingsChanges.Count > 0 ? current.PomodoroDefaults : proposed.PomodoroDefaults,
            Escalation = settingsChanges.Count > 0 ? current.Escalation : proposed.Escalation,
            Bypass = settingsChanges.Count > 0 ? current.Bypass : proposed.Bypass,
            Locks = settingsChanges.Count > 0 ? current.Locks : proposed.Locks,
        };

        return new ConfigurationSplit(applyNow, waiting);
    }
}
