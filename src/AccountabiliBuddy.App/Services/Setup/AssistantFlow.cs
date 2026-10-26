using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// Which pages the setup assistant shows, in order. The first run asks three questions (what kind of computer, link to
/// your other devices, a daily goal); the full setup (More → Setup) keeps every page for imports and browsers.
/// </summary>
public static class AssistantFlow
{
    /// <param name="quick">The first run's three questions.</param>
    /// <param name="hasIdentity">Linking devices is available (ADR 22).</param>
    /// <param name="hasTypes">Computer types are available.</param>
    /// <param name="hasUsage">Another blocker's statistics were found (the Usage page).</param>
    /// <param name="joined">Joined a device group: the profiles come from the other devices, so the import pages are skipped.</param>
    /// <param name="hasColdTurkey">Cold Turkey is installed: the full setup offers to hold its keys (ADR 39).</param>
    /// <param name="needsSpareKey">No spare key written down yet: the spare key page follows.</param>
    public static IReadOnlyList<AssistantStep> Steps(bool quick, bool hasIdentity, bool hasTypes, bool hasUsage, bool joined,
        bool hasColdTurkey = false, bool needsSpareKey = false)
    {
        if (quick)
        {
            var steps = new List<AssistantStep>();
            if (hasTypes)
            {
                steps.Add(AssistantStep.ComputerType);
            }

            if (hasIdentity)
            {
                steps.Add(AssistantStep.LinkFirst);
            }

            steps.Add(AssistantStep.Goal);
            steps.Add(AssistantStep.Done);
            return steps;
        }

        return Enum.GetValues<AssistantStep>()
            .Where(step => step != AssistantStep.Goal)
            .Where(step => step != AssistantStep.Usage || hasUsage)
            .Where(step => step != AssistantStep.LinkFirst || hasIdentity)
            .Where(step => step != AssistantStep.ComputerType || hasTypes)
            .Where(step => !joined || step is not (AssistantStep.Import or AssistantStep.Usage or AssistantStep.Review))
            .Where(step => step != AssistantStep.ColdTurkeyKeys || hasColdTurkey)
            .Where(step => step != AssistantStep.SpareKey || (hasColdTurkey && needsSpareKey))
            .ToList();
    }
}
