namespace AccountabiliBuddy.App.Services.Today;

/// <summary>
/// The parts of the PC's Today sidebar the person can arrange (Settings › Today). The session card (or the start card) and This
/// session are not sidebar parts: they sit in the same slots in and out of a session (CN §1 rule 7) and can each be hidden
/// (<see cref="TodayMainParts"/>). Daily steps left Today in TD1: steps are seen through a Steps goal. Details per part:
/// <see cref="TodayParts"/>.
/// </summary>
public enum TodayPart
{
    NextUp,
    Goals,
    Comebacks,
    Timers,
    Lunch,
    Beeminder,
    CalendarChanges,
}
