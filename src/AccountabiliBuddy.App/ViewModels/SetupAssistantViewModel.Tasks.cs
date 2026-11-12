namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// The setup assistant's Tasks redesign pages (ADR 76, 78; Phase 8): the introduction, where the Tasks view comes from (owner,
/// 2026-11-11), which devices ring for task reminders (13s) and task reminders in Google Calendar. The chooser and the ringing devices
/// are also in the first run (<see cref="Services.Setup.AssistantFlow"/>); all four are pages of new features for existing installs.
/// </summary>
public sealed partial class SetupAssistantViewModel
{
    /// <summary>"Where should your Tasks view come from?"; null without the task list (skips the page).</summary>
    public TaskViewChooserViewModel? TaskViewChooser { get; init; }

    /// <summary>The account's "Task reminders ring on" switches (Settings › Alerts &amp; on-screen's); null without the task list.</summary>
    public TaskReminderAlertsViewModel? TaskRingDevices { get; init; }

    /// <summary>Google Calendar's OK for task reminders, or Nowhere; null without the task list.</summary>
    public TaskExportSetupViewModel? TaskExport { get; init; }

    public bool IsTasksRedesign => Step == AssistantStep.TasksRedesign;

    public bool IsTaskViewChooser => Step == AssistantStep.TaskViewChooser;

    public bool IsTaskRingDevices => Step == AssistantStep.TaskRingDevices;

    public bool IsTaskCalendarExport => Step == AssistantStep.TaskCalendarExport;

    // Only phones' switches move (a PC always rings for its own reminders): without one the question has nothing to ask.
    private bool OffersRingChoice => TaskRingDevices?.HasPhones == true;

    private int _accountRead;

    private void OnTaskStepShown(AssistantStep step)
    {
        if (step == AssistantStep.TaskViewChooser && TaskViewChooser is not null)
        {
            _ = ShowViewChooserAsync(TaskViewChooser);
        }

        if (step == AssistantStep.TaskRingDevices)
        {
            TaskRingDevices?.Load();
        }

        if (step == AssistantStep.TaskCalendarExport && TaskExport is not null)
        {
            TaskExport.Load();
            _ = TaskExport.CheckAsync();
        }
    }

    // Just joined: the account's copy comes with the other devices' files, so they are read once before the choice is shown.
    private async Task ShowViewChooserAsync(TaskViewChooserViewModel chooser)
    {
        chooser.Refresh();
        if (!_joined || ReadOtherDevicesNow is null || HasSyncMethod?.Invoke() == false)
        {
            return;
        }

        int read = ++_accountRead;
        StatusMessage = "Getting the copy stored in your account…";
        try
        {
            await ReadOtherDevicesNow();
        }
        finally
        {
            if (read == _accountRead)
            {
                StatusMessage = null;
                bool wasFresh = chooser.IsFreshPicked;
                chooser.Refresh();
                if (wasFresh && chooser.HasAccountCopy)
                {
                    chooser.IsAccountPicked = true;
                }

                TaskRingDevices?.Load();
                OnPropertyChanged(nameof(StepNumberText));
            }
        }
    }

    /// <summary>Marks a What's new feature as dealt with (<see cref="Services.Setup.SetupFeaturesService.SkipAsync"/>); set by the app.</summary>
    public Func<string, Task>? SkipFeature { get; init; }

    // Next on a Tasks page: the chooser applies the picked copy, the ringing devices are kept as shown (a choice for the account).
    // False keeps the page (nothing could be applied yet, with why). Start fresh over an account that holds nothing writes nothing (the
    // chooser's rule: a stamp made now would later reset the group's choice); in What's new the feature is then marked dealt with. A PC
    // that has just joined a group may not have the account's ring-on list yet, so "as shown" writes nothing there (a switch the user
    // turned was already saved).
    private async Task<bool> FinishTaskStepAsync()
    {
        switch (Step)
        {
            case AssistantStep.TaskRingDevices when _joined:
                return true;
            case AssistantStep.TaskViewChooser when TaskViewChooser is not null:
                if (!await TaskViewChooser.ApplyAsync())
                {
                    StatusMessage = TaskViewChooser.StatusText;
                    return false;
                }

                if (!TaskViewChooser.LastApplyWrote && _whatsNew is not null && SkipFeature is not null)
                {
                    await SkipFeature(Services.Setup.SetupFeatures.TasksRedesignId);
                }

                return true;
            case AssistantStep.TaskRingDevices when TaskRingDevices is not null:
                await TaskRingDevices.ConfirmRingOnAsync();
                return true;
            default:
                return true;
        }
    }
}
