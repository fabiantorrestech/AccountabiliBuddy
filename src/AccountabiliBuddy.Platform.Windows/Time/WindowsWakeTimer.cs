using AccountabiliBuddy.Core.Ports;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AccountabiliBuddy.Platform.Windows.Time;

/// <summary>
/// A waitable timer set to resume the computer (SetWaitableTimer with fResume), kept open until the next time is set or
/// it's cancelled. Windows wakes for it only when the power plan's "Allow wake timers" is on (read for Problem()).
/// </summary>
public sealed unsafe class WindowsWakeTimer : IWakeTimer, IDisposable
{
    // Sleep → "Allow wake timers" in the power plan.
    private static readonly Guid SleepSubgroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid AllowWakeTimers = new("bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d");
    private const uint TimerAllAccess = 0x1F0003;

    private SafeFileHandle? _timer;

    public bool WakeAt(DateTimeOffset at)
    {
        Cancel();
        SafeFileHandle timer = PInvoke.CreateWaitableTimerEx(null, (string?)null, 0, TimerAllAccess);
        if (timer.IsInvalid)
        {
            return false;
        }

        // A positive due time is absolute, in 100 ns ticks since 1601 (UTC).
        long due = at.UtcDateTime.ToFileTimeUtc();
        if (!PInvoke.SetWaitableTimer(timer, due, 0, null, null, true))
        {
            timer.Dispose();
            return false;
        }

        _timer = timer;
        return true;
    }

    public void Cancel()
    {
        if (_timer is null)
        {
            return;
        }

        PInvoke.CancelWaitableTimer(_timer);
        _timer.Dispose();
        _timer = null;
    }

    public string? Problem()
    {
        Guid* scheme = null;
        try
        {
            if (PInvoke.PowerGetActiveScheme(null, out scheme) != WIN32_ERROR.ERROR_SUCCESS || scheme is null)
            {
                return null;
            }

            Guid active = *scheme;
            PInvoke.PowerReadACValueIndex(null, active, SleepSubgroup, AllowWakeTimers, out uint plugged);
            PInvoke.PowerReadDCValueIndex(null, active, SleepSubgroup, AllowWakeTimers, out uint battery);
            return (plugged, battery) switch
            {
                (0, 0) => "Windows' power plan doesn't allow wake timers: a sleeping PC won't wake to ring. Turn on Power Options → Sleep → Allow wake timers.",
                (0, _) => "Windows' power plan doesn't allow wake timers when plugged in: a sleeping PC won't wake to ring.",
                (_, 0) => "On battery, Windows' power plan doesn't allow wake timers: a sleeping laptop won't wake to ring unless it's plugged in.",
                _ => null,
            };
        }
        finally
        {
            if (scheme is not null)
            {
                PInvoke.LocalFree((HLOCAL)scheme);
            }
        }
    }

    public void Dispose()
    {
        Cancel();
    }
}
