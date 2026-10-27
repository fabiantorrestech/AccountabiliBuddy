using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// The PC's open end-early offer for the phone (ADR 42) and the phase it was made in. A return from stepping away
/// moves the phase's start, so the phase is told by its mode, kind, profile and cycle count instead. Kept in memory:
/// after a restart the phone asks again. UI thread only.
/// </summary>
public sealed class SyncEndEarlyOffers
{
    private SyncEndEarlyOffer? _offer;
    private PhaseKey? _phase;

    /// <summary>The open offer, whether or not it ran out; null when there's none.</summary>
    public SyncEndEarlyOffer? Open => _offer;

    /// <summary>The offer for this PC's file: null when there's none or it ran out at <paramref name="now"/>.</summary>
    public SyncEndEarlyOffer? Current(DateTimeOffset now) =>
        _offer is { } offer && now <= offer.AllowedFrom + SyncEndEarlyOffer.ValidFor ? offer : null;

    /// <summary>A new offer for the running phase of <paramref name="state"/>, replacing any other.</summary>
    public SyncEndEarlyOffer Make(SessionState state, DateTimeOffset allowedFrom, int minReasonLength, string? note, DateTimeOffset now)
    {
        _offer = new SyncEndEarlyOffer(Guid.NewGuid(), state.PhaseStartedAt ?? now, allowedFrom, minReasonLength, note);
        _phase = PhaseKey.Of(state);
        return _offer;
    }

    /// <summary>True while <paramref name="state"/> is still the session and phase the offer was made in.</summary>
    public bool BelongsTo(SessionState state) => _offer is not null && state.IsActive && PhaseKey.Of(state) == _phase;

    public void Clear()
    {
        _offer = null;
        _phase = null;
    }

    private sealed record PhaseKey(SessionMode? Mode, SessionPhase Phase, Guid? ProfileId, int CompletedFocusCycles)
    {
        // Planning leads straight into the same focus phase: an offer made while planning stays good when focus begins.
        public static PhaseKey Of(SessionState state) =>
            new(state.Mode, state.IsPlanning ? SessionPhase.Focus : state.Phase, state.ProfileId, state.CompletedFocusCycles);
    }
}
