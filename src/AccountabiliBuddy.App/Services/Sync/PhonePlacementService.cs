using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// Nags from the phone's placement (phone placement): during a real focus session on this PC (planning counts; a break and
/// a test session don't), the newest phone placement whose session start matches this session says where the phone is.
/// In a pocket: a notice at most every 10 minutes. Out (in use or face up) without work use: a notice at once, a
/// reminder that stays until "Got it" after a minute. When a work-use stretch ends: one notice. Bag, desk and unknown:
/// nothing. Use from the UI thread.
/// </summary>
public sealed class PhonePlacementService : IDisposable
{
    // The phone echoes the session's start back; a rounding difference is fine, a different session is not.
    private static readonly TimeSpan StartTolerance = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PocketEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OutBeforeReminder = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly ReceivedDevicesService _received;
    private readonly Func<DateTimeOffset?> _sessionStart;
    private readonly Action<string, string> _notify;
    private readonly Action<string, string> _remind;

    private DateTimeOffset? _trackedStart;
    private DateTimeOffset? _lastPocketNotice;
    private DateTimeOffset? _outSince;
    private bool _reminded;
    private DateTimeOffset? _workUseEnd;
    private bool _workUseEndTold;

    public PhonePlacementService(
        SessionController sessions, ReceivedDevicesService received, Func<DateTimeOffset?> sessionStart,
        Action<string, string> notify, Action<string, string> remind)
    {
        _sessions = sessions;
        _received = received;
        _sessionStart = sessionStart;
        _notify = notify;
        _remind = remind;
        _sessions.Ticked += OnTicked;
        _received.Changed += OnReceivedChanged;
    }

    public void Dispose()
    {
        _sessions.Ticked -= OnTicked;
        _received.Changed -= OnReceivedChanged;
    }

    private void OnTicked(object? sender, EventArgs e) => Run();

    private void OnReceivedChanged(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(Run);

    /// <summary>Looks at the phone's placement; runs every second and when a phone's file comes.</summary>
    public void Run()
    {
        SessionState state = _sessions.State;
        DateTimeOffset? start = _sessionStart();
        // A real session in planning or focus; a break (away) and a test session get no nags.
        if (!state.IsRealSession || !state.IsEnforcing || state.IsAway || start is not DateTimeOffset sessionStart)
        {
            Reset(null);
            return;
        }

        if (_trackedStart is not DateTimeOffset tracked || (tracked - sessionStart).Duration() > StartTolerance)
        {
            Reset(sessionStart);
        }

        DateTimeOffset now = _sessions.Now;
        if (Current(sessionStart) is not SyncPlacement placement)
        {
            ClearOut();
            return;
        }

        if (placement.WorkUseUntil is DateTimeOffset until && (_workUseEnd is null || until > _workUseEnd))
        {
            _workUseEnd = until;
            _workUseEndTold = false;
        }

        bool onWorkUse = _workUseEnd is DateTimeOffset end && now < end;
        if (placement.State == SyncPlacement.Pocket)
        {
            ClearOut();
            NoticePocket(now);
        }
        else if (placement.IsOut)
        {
            NagOut(onWorkUse, now);
        }
        else
        {
            ClearOut();
        }

        TellWorkUseOver(placement, now);
    }

    private void NoticePocket(DateTimeOffset now)
    {
        if (_lastPocketNotice is DateTimeOffset last && now - last < PocketEvery)
        {
            return;
        }

        _lastPocketNotice = now;
        _notify("Phone in your pocket", "A bag or face down on the desk is better.");
    }

    private void NagOut(bool onWorkUse, DateTimeOffset now)
    {
        if (onWorkUse)
        {
            // Allowed for now; it is told again from when the work use ends.
            ClearOut();
            return;
        }

        if (_outSince is null)
        {
            _outSince = now;
            _reminded = false;
            // The "work use is over" notice already covers a stretch that is still out when its time is up.
            if (_workUseEnd is DateTimeOffset end && !_workUseEndTold && now >= end)
            {
                return;
            }

            _notify("Phone's out", "Put it away (bag or face down).");
            return;
        }

        if (!_reminded && now - _outSince.Value >= OutBeforeReminder)
        {
            _reminded = true;
            _remind("Still on your phone", "It opens Essentials in 2 min.");
        }
    }

    // Once, when a work-use stretch is over and the phone is still not put back.
    private void TellWorkUseOver(SyncPlacement placement, DateTimeOffset now)
    {
        if (_workUseEnd is not DateTimeOffset end || _workUseEndTold || now < end)
        {
            return;
        }

        _workUseEndTold = true;
        if (placement.IsOut || placement.State == SyncPlacement.Pocket)
        {
            _notify("Work use is over", "Put the phone back.");
        }
    }

    // The newest placement from a phone whose session start is this session's.
    private SyncPlacement? Current(DateTimeOffset start)
    {
        return _received.Devices
            .Where(device => device.File.Device.Kind == SyncDeviceCard.KindPhone && device.File.Placement is not null)
            .OrderByDescending(device => device.File.Device.WrittenAt)
            .Select(device => device.File.Placement!)
            .FirstOrDefault(placement => (placement.SessionStart - start).Duration() <= StartTolerance);
    }

    private void ClearOut()
    {
        _outSince = null;
        _reminded = false;
    }

    private void Reset(DateTimeOffset? start)
    {
        _trackedStart = start;
        _lastPocketNotice = null;
        _workUseEnd = null;
        _workUseEndTold = false;
        ClearOut();
    }
}
