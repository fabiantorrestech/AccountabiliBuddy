using AccountabiliBuddy.App.Services.Sync;
using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Engine;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// Everything the Stats page draws from, read once per refresh: the counted focus across the user's devices for the days
/// History keeps, the drift visits, and what the phone already syncs (pickups during sessions, the smart bedtime's numbers).
/// Nothing here is new data: it is what History shows, added up differently.
/// </summary>
/// <param name="Days">One entry per date from <paramref name="FirstDay"/> to <paramref name="Today"/>, oldest first.</param>
/// <param name="FirstData">The first date with any history (a session on any device); earlier dates have no data at all, never zeros.</param>
/// <param name="LoggingOn">Activity history is on: with it off no drift is stored, so a drift count would be a made-up zero.</param>
/// <param name="Drift">Visits to distracting sites inside sessions, ordered by start.</param>
/// <param name="PhoneCounts">A phone in the group counts pickups.</param>
/// <param name="Pickups">The phone's count for each of this PC's sessions.</param>
/// <param name="Records">This PC's sessions in the kept days, newest first.</param>
internal sealed record StatsData(
    DateOnly Today,
    DateOnly FirstDay,
    DateOnly FirstData,
    bool LoggingOn,
    TimeZoneInfo Zone,
    DateTime NowLocal,
    TimeSpan Goal,
    IReadOnlyList<LocalWindow> Windows,
    IReadOnlyList<DeepWorkDay> Days,
    IReadOnlyList<DriftStretch> Drift,
    bool PhoneCounts,
    IReadOnlyList<SyncPickup> Pickups,
    SyncSmartBedtime? Bedtime,
    IReadOnlyList<SessionRecord> Records,
    IReadOnlyList<ReceivedSession> Received)
{
    /// <summary>The local date a moment falls on.</summary>
    public DateOnly DateOf(DateTimeOffset time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, Zone).DateTime);

    /// <summary>The day's entry, or null for a date before the first one with history (or after today).</summary>
    public DeepWorkDay? DayOf(DateOnly date)
    {
        int index = date.DayNumber - FirstDay.DayNumber;
        return date >= FirstData && index >= 0 && index < Days.Count ? Days[index] : null;
    }

    /// <summary>Every day from the first with history up to today, oldest first.</summary>
    public IReadOnlyList<DeepWorkDay> HistoryDays => Days.Where(day => day.Date >= FirstData).ToList();

    /// <summary>The days from <paramref name="from"/> to <paramref name="to"/> that have happened and that have history.</summary>
    public IReadOnlyList<DeepWorkDay> DaysBetween(DateOnly from, DateOnly to)
    {
        return Days.Where(day => day.Date >= from && day.Date <= to && day.Date <= Today && day.Date >= FirstData).ToList();
    }

    /// <summary>The drift visits that began on the dates <paramref name="from"/> to <paramref name="to"/>.</summary>
    public IReadOnlyList<DriftStretch> DriftBetween(DateOnly from, DateOnly to)
    {
        return Drift.Where(visit => DateOf(visit.Start) >= from && DateOf(visit.Start) <= to).ToList();
    }

    /// <summary>The phone's counts for sessions that began on the dates <paramref name="from"/> to <paramref name="to"/>.</summary>
    public IReadOnlyList<SyncPickup> PickupsBetween(DateOnly from, DateOnly to)
    {
        return Pickups.Where(pickup => DateOf(pickup.SessionStart) >= from && DateOf(pickup.SessionStart) <= to).ToList();
    }
}
