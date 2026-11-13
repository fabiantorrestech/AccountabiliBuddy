using AccountabiliBuddy.App.ViewModels;

namespace AccountabiliBuddy.App.Services.Setup;

/// <summary>
/// Which pages the setup assistant shows, in order. The first run asks three questions (what kind of computer, link to
/// your other devices, a daily goal), plus where the Tasks view comes from and, after joining a group with a phone, which devices ring
/// for task reminders; the full setup (More → Setup) keeps every page for imports and browsers.
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
    /// <param name="hasRestore">This PC is in a device group and backups can be opened (ADR 55): "Restore from an older device" follows linking.</param>
    /// <param name="hasTaskList">The task list is in use (ADR 76): the first run asks where the Tasks view comes from.</param>
    /// <param name="ringChoice">Joined a device group that has a phone: the first run asks which devices ring for task reminders (13s).</param>
    public static IReadOnlyList<AssistantStep> Steps(bool quick, bool hasIdentity, bool hasTypes, bool hasUsage, bool joined,
        bool hasColdTurkey = false, bool needsSpareKey = false, bool hasRestore = false, bool hasTaskList = false, bool ringChoice = false)
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

            if (hasRestore)
            {
                steps.Add(AssistantStep.Restore);
            }

            // After linking and restoring: the account's copy may have come with the other devices' files by then.
            if (hasTaskList)
            {
                steps.Add(AssistantStep.TaskViewChooser);
            }

            if (hasTaskList && joined && ringChoice)
            {
                steps.Add(AssistantStep.TaskRingDevices);
            }

            // ADR 68: always asked. Left as shown, a device that joined a group follows the mode its devices chose; a choice made
            // here goes to them (ProductaBuddy after their wait).
            steps.Add(AssistantStep.Guidance);
            steps.Add(AssistantStep.Goal);
            steps.Add(AssistantStep.Done);
            return steps;
        }

        return Enum.GetValues<AssistantStep>()
            .Where(step => step != AssistantStep.Goal)
            .Where(step => step != AssistantStep.OwnRelay)
            .Where(step => step != AssistantStep.DpAssistant)
            .Where(step => step != AssistantStep.StartOfDay)
            .Where(step => step != AssistantStep.Appearance)
            .Where(step => step != AssistantStep.Tasks)
            .Where(step => step != AssistantStep.PlanHelper)
            .Where(step => step != AssistantStep.OverlayTaskbar)
            .Where(step => step != AssistantStep.PanelCtrlAlt)
            .Where(step => step != AssistantStep.CalendarRules)
            .Where(step => step != AssistantStep.ShutdownRoutine)
            .Where(step => step != AssistantStep.QuitSafeguard)
            .Where(step => step is not (AssistantStep.TasksRedesign or AssistantStep.TaskViewChooser or AssistantStep.TaskRingDevices or AssistantStep.TaskCalendarExport))
            .Where(step => step != AssistantStep.GoalsIntro)
            .Where(step => step != AssistantStep.TodayChoices)
            .Where(step => step != AssistantStep.Usage || hasUsage)
            .Where(step => step != AssistantStep.LinkFirst || hasIdentity)
            .Where(step => step != AssistantStep.Restore || hasRestore)
            .Where(step => step != AssistantStep.ComputerType || hasTypes)
            .Where(step => !joined || step is not (AssistantStep.Import or AssistantStep.Usage or AssistantStep.Review))
            .Where(step => step != AssistantStep.ColdTurkeyKeys || hasColdTurkey)
            .Where(step => step != AssistantStep.SpareKey || (hasColdTurkey && needsSpareKey))
            .ToList();
    }
}
