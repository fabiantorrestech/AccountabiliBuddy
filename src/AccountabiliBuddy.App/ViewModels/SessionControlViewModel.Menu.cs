using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 (plan R2.2, brief §3.6 and §4.2, board Today-PC-Session-Menu): the title bar's ⋯ › Session menu in the shared menu look.
public sealed partial class SessionControlViewModel
{
    /// <summary>
    /// The Session menu: the heading "Session · ends 1:23 PM"; Step away (its note says whether the timer keeps running, ADR 77) or
    /// I'm back / Resume, Pause timer, Side task…, Add 10 minutes │ End and mark the rest unfinished (unfinished tasks go back to Queued)
    /// │ End session (red, last). Side task… is not on the board; it stays so nothing the old menu offered is lost. Each entry runs the
    /// command the old menu ran: both ends go through the app's end flow (<see cref="SessionEndFlow"/>, the same wait and reason), so
    /// nothing here shortens a wait or unlocks anything. Empty with no session.
    /// </summary>
    public IReadOnlyList<TaskMenuEntry> MenuEntries()
    {
        if (!IsRunningOrAway)
        {
            return [];
        }

        var entries = new List<TaskMenuEntry>
        {
            new(EndsText.Length > 0 ? $"Session · {EndsText}" : "Session", IsHeading: true),
            new(MainActionText, MainActionCommand, Icon: MainActionIcon, Note: HasMainActionSub ? MainActionSubText : null),
        };
        if (CanPause)
        {
            entries.Add(new TaskMenuEntry("Pause timer", PauseTimerCommand, Icon: MaterialIconKind.Pause, Note: "Freezes the countdown until Resume"));
        }

        if (CanSideTask)
        {
            entries.Add(new TaskMenuEntry("Side task…", SideTaskCommand, Icon: MaterialIconKind.CoffeeOutline, Note: "A small errand at the desk · 5, 10 or 15 min"));
        }

        if (CanExtend)
        {
            entries.Add(new TaskMenuEntry("Add 10 minutes", ExtendFocusCommand, Icon: MaterialIconKind.TimerPlusOutline));
        }

        entries.Add(TaskMenuEntry.Separator);
        if (CanEndToQueued)
        {
            entries.Add(new TaskMenuEntry("End and mark the rest unfinished", EndToQueuedCommand, Icon: MaterialIconKind.FlagOutline,
                Note: "Unfinished actions go back to Queued"));
            entries.Add(TaskMenuEntry.Separator);
        }

        entries.Add(new TaskMenuEntry(EndSessionText.TrimEnd('…'), EndSessionCommand, Icon: MaterialIconKind.StopCircleOutline, IsDanger: true,
            Note: "The normal end-early flow"));
        return entries;
    }
}
