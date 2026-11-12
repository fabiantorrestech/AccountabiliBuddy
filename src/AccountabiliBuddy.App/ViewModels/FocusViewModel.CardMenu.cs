using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 (brief §4.2): the ⋯ menu of Today's session card, in the shared menu look (TaskMenus): what used to sit in the old main
// card's ⋯ menu and its "+10 min" button. Every entry runs the command it ran before; End session is the same end flow.
public sealed partial class FocusViewModel
{
    /// <summary>The view answers this entry itself (it opens the note line on the ⋯ button).</summary>
    public const string NoteAction = "note";

    /// <summary>The session card's ⋯ menu: Interrupted, Side task, +10 minutes, the note, the all-done and overlay toggles, End session (red, last).</summary>
    public IReadOnlyList<TaskMenuEntry> CardMenu()
    {
        var entries = new List<TaskMenuEntry>();
        if (!IsAway)
        {
            entries.Add(new TaskMenuEntry("Interrupted", InterruptedCommand, Icon: MaterialIconKind.AccountVoice,
                Note: "Someone at your desk or a call: kept apart in History"));
            entries.Add(new TaskMenuEntry("Side task…", SideTaskCommand, Icon: MaterialIconKind.CoffeeOutline,
                Note: "A small errand at the desk · 5, 10 or 15 min"));
        }

        if (ShowsExtend)
        {
            entries.Add(new TaskMenuEntry("Add 10 minutes", ExtendFocusCommand, Icon: MaterialIconKind.TimerPlusOutline));
        }

        if (Capture is not null)
        {
            entries.Add(new TaskMenuEntry("Note for this session…", ViewAction: NoteAction, Icon: MaterialIconKind.PencilOutline));
        }

        if (Checklist is { ShowsNotifyChoice: true } checklist)
        {
            entries.Add(new TaskMenuEntry("Tell me when all tasks are done", new RelayCommand(() => checklist.NotifyAllDone = !checklist.NotifyAllDone),
                IsChecked: checklist.NotifyAllDone, Icon: MaterialIconKind.BellOutline));
        }

        if (TimerToggle is { IsVisible: true } overlay)
        {
            entries.Add(new TaskMenuEntry(overlay.ButtonText, overlay.ToggleCommand, Icon: MaterialIconKind.MonitorEye));
        }

        if (entries.Count > 0)
        {
            entries.Add(TaskMenuEntry.Separator);
        }

        entries.Add(new TaskMenuEntry(EndSessionText.TrimEnd('…'), EndSessionCommand, Icon: MaterialIconKind.StopCircleOutline, IsDanger: true,
            Note: "The normal end-early flow"));
        return entries;
    }
}
