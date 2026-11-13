using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Feeds <see cref="LunchListening"/> from this PC's own state: today's lunch, the lunch in each other active device's latest received file, and today's
/// planned lunch windows (the plan's flexible break and fixed lunch). The Drive, relay and folder services ask it whether to poll fast; it holds
/// nothing and does no I/O (the received files are already in memory), so it is cheap to ask on every timer tick.
/// </summary>
public sealed class LunchListeningSource(
    LunchService lunch, ReceivedDevicesService received, Func<DayPlan> plan, TimeProvider time)
{
    public bool IsListening()
    {
        DateTimeOffset now = time.GetUtcNow();
        DateTime local = time.GetLocalNow().DateTime;
        DateOnly today = DateOnly.FromDateTime(local);
        DayPlan current = plan();
        var windows = new List<LocalWindow>(2);
        if (current.FlexibleBreakOn(today) is FlexibleBreak flexible)
        {
            windows.Add(flexible.Window);
        }

        if (current.FixedLunchOn(today) is FixedLunch fixedLunch)
        {
            windows.Add(fixedLunch.Window);
        }

        return LunchListening.IsListening(now, local, lunch.Taken, received.Active.Select(device => device.File.Lunch), windows);
    }
}
