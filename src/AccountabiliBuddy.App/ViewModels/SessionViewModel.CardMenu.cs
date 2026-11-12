using Material.Icons;

namespace AccountabiliBuddy.App.ViewModels;

// Round 2 (brief §4.2): the ⋯ menu of Today's start card (nothing running), in the shared menu look (TaskMenus). It holds what the old
// idle card's "Other ways to start" menu, "Start shutdown", "Back to work" and the fold's "Plan day(s)…" did, with the same commands.
public sealed partial class SessionViewModel
{
    /// <summary>The view answers these entries itself (they open a form on the ⋯ button).</summary>
    public const string PomodoroAction = "pomodoro";

    public const string StartOptionsAction = "startOptions";

    /// <summary>The start card's ⋯ menu.</summary>
    public IReadOnlyList<TaskMenuEntry> StartMenu()
    {
        var entries = new List<TaskMenuEntry>
        {
            new("Other ways to start", IsHeading: true),
            new("Quick Start with Default", QuickStart.QuickStartDefaultCommand, Icon: MaterialIconKind.Play,
                Note: "Your default profile for the length shown"),
            new("Pomodoro…", ViewAction: PomodoroAction, Icon: MaterialIconKind.TimerOutline),
            new("No set end", QuickStart.StartOpenEndedCommand, Icon: MaterialIconKind.AllInclusive,
                Note: "Runs until you end it; no countdown"),
            new("Profile, length, break…", ViewAction: StartOptionsAction, Icon: MaterialIconKind.TimerCogOutline,
                OpensMore: true),
            TaskMenuEntry.Separator,
            new("Plan day(s)…", PlanDayCommand, Icon: MaterialIconKind.CalendarEditOutline,
                Note: "An adjusted workday or an off-day"),
            new("Start shutdown", StartShutdownCommand, Icon: MaterialIconKind.WeatherSunset,
                Note: "Wrap up the day: capture, look ahead, plan tomorrow"),
        };
        if (CanGoBackToWork)
        {
            entries.Add(new TaskMenuEntry("Back to work", BackToWorkCommand, Icon: MaterialIconKind.BriefcaseOutline,
                Note: "Make the rest of the planned work block work again"));
        }

        return entries;
    }
}
