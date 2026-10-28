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
/// <param name="Drift">Visits to distracting sites inside sessions, ordered by start.</param>
/// <param name="PhoneCounts">A phone in the group counts pickups.</param>
/// <param name="Pickups">The phone's count for each of this PC's sessions.</param>
/// <param name="Records">This PC's sessions in the kept days, newest first.</param>
internal sealed record StatsData(
    DateOnly Today,
    DateOnly FirstDay,
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

    /// <summary>The day's entry, or null for a date History doesn't have.</summary>
    public DeepWorkDay? DayOf(DateOnly date)
    {
        int index = date.DayNumber - FirstDay.DayNumber;
        return index >= 0 && index < Days.Count ? Days[index] : null;
    }

    /// <summary>The days from <paramref name="from"/> to <paramref name="to"/> that have happened and that History has.</summary>
    public IReadOnlyList<DeepWorkDay> DaysBetween(DateOnly from, DateOnly to)
    {
        return Days.Where(day => day.Date >= from && day.Date <= to && day.Date <= Today).ToList();
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
