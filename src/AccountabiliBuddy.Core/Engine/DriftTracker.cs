using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.Core.Engine;

/// <summary>
/// Tracks, per monitor, how long the current classification has lasted and turns that
/// into an escalation level. Switching between two distracting windows does not reset
/// the timer; only a change of classification does. Unknown activity escalates too once it outlasts the grace
/// period, timed from the end of the grace period.
/// Not thread-safe: call from a single thread.
/// </summary>
public sealed class DriftTracker
{
    private readonly EscalationPolicy _policy;
    private readonly Dictionary<string, MonitorDrift> _driftByMonitor = new(StringComparer.Ordinal);

    public DriftTracker(EscalationPolicy policy)
    {
        _policy = policy;
    }

    /// <param name="holdUnknownsUntil">Plan first: until then unknown activity isn't asked about or escalated, and its
    /// grace period counts from then.</param>
    public DriftAssessment Observe(
        ActivitySnapshot snapshot, ClassificationResult result, TimeSpan unknownGracePeriod, DateTimeOffset? holdUnknownsUntil = null)
    {
        bool hasPrevious = _driftByMonitor.TryGetValue(snapshot.MonitorId, out MonitorDrift? drift);
        bool classificationChanged = !hasPrevious
            || drift!.Classification != result.Classification
            || snapshot.Timestamp < drift.Since;

        if (classificationChanged)
        {
            drift = new MonitorDrift(result.Classification, snapshot.Timestamp);
            _driftByMonitor[snapshot.MonitorId] = drift;
        }

        TimeSpan duration = snapshot.Timestamp - drift!.Since;

        // Unknown time counts from the later of when it started and the end of the plan-first hold.
        DateTimeOffset unknownSince = holdUnknownsUntil is DateTimeOffset hold && hold > drift.Since ? hold : drift.Since;
        TimeSpan unknownFor = snapshot.Timestamp - unknownSince;
        bool needsClassification = result.Classification == Classification.Unknown
            && unknownFor >= unknownGracePeriod;

        EscalationLevel level = result.Classification switch
        {
            Classification.Distracting => _policy.Decide(duration),
            Classification.Unknown when needsClassification => _policy.Decide(unknownFor - unknownGracePeriod),
            _ => EscalationLevel.None,
        };

        return new DriftAssessment(
            snapshot.MonitorId,
            result.Classification,
            duration,
            level,
            needsClassification,
            result.MatchedRule);
    }

    /// <summary>Drops state for a monitor that was disconnected.</summary>
    public void Forget(string monitorId)
    {
        _driftByMonitor.Remove(monitorId);
    }

    /// <summary>Clears all state, e.g. when a focus phase ends.</summary>
    public void Reset()
    {
        _driftByMonitor.Clear();
    }

    private sealed record MonitorDrift(Classification Classification, DateTimeOffset Since);
}
