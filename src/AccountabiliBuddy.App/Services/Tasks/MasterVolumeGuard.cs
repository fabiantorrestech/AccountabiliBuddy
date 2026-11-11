using AccountabiliBuddy.Core.Ports;
using AccountabiliBuddy.Core.Storage;
using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// "Ring even when the PC is muted": while a task reminder rings, raises or unmutes the default output (only up, never down), and
/// afterwards puts the user's own level and mute back. What to put back is written to disk (document "tasks.volumeRestore") before the
/// output is touched, so a crash or kill mid-ring is undone on the next start (<see cref="RestoreLeftoverBlocking"/>). The output is
/// found again by its id, so a switch of the default output mid-ring (headphones, a dock) still restores the one that was raised. The
/// state stored as raised is read back after the change (a device with coarse steps lands on its own level). An output the user changed
/// meanwhile is left as they set it (<see cref="MasterVolumePlan.ShouldRestore"/>). Nothing here throws. Use from the UI thread only.
/// </summary>
public sealed class MasterVolumeGuard
{
    public const string DocumentKey = "tasks.volumeRestore";

    private readonly IMasterVolume _volume;
    private readonly SqliteJsonDocumentStore _store;
    private Raise? _held;

    public MasterVolumeGuard(IMasterVolume volume, SqliteJsonDocumentStore store)
    {
        _volume = volume;
        _store = store;
    }

    /// <summary>What a raise changed: the user's state to put back, and the state the raise left (as read back).</summary>
    public sealed record Raise(MasterVolumeState Original, MasterVolumeState Raised);

    /// <summary>True while the output is raised and waiting to be put back.</summary>
    public bool IsRaised => _held is not null;

    /// <summary>Puts back what a run that ended badly left raised (a crash during a ring). Call once at start.</summary>
    public void RestoreLeftoverBlocking()
    {
        try
        {
            Raise? left = Task.Run(() => _store.LoadAsync<Raise>(DocumentKey)).GetAwaiter().GetResult();
            if (left is null)
            {
                return;
            }

            PutBack(left);
            Task.Run(() => _store.DeleteAsync(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Nothing to put back that can be read.
        }
    }

    /// <summary>Raises the output for a ring at <paramref name="ringVolume"/> (0 to 100).</summary>
    /// <returns>False when the output could not be raised (the caller rings normally and may say "Couldn't raise the volume"); true when raised or already loud enough.</returns>
    public bool RaiseFor(int ringVolume)
    {
        try
        {
            return RaiseCore(ringVolume);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Puts the user's level and mute back (when the output still is as the raise left it).</summary>
    public void Restore()
    {
        try
        {
            if (_held is not Raise raise)
            {
                return;
            }

            _held = null;
            PutBack(raise);
            DeleteSaved();
        }
        catch (Exception)
        {
            // Left raised at worst; the saved restore puts it back at the next start.
        }
    }

    private bool RaiseCore(int ringVolume)
    {
        MasterVolumeState? current = _volume.Read();
        if (current is null)
        {
            return false;
        }

        // Already raised for an earlier ring: raise again only when this ring needs more than that left.
        if (_held is Raise held)
        {
            MasterVolumeState? now = _volume.Read(held.Raised.DeviceId);
            if (now is null || MasterVolumePlan.Raise(now, ringVolume) is not MasterVolumeState more)
            {
                return true;
            }

            if (!_volume.Apply(more.DeviceId, more.Level, more.Muted) || _volume.Read(more.DeviceId) is not MasterVolumeState moreActual)
            {
                return false;
            }

            _held = held with { Raised = moreActual };
            Save(_held);
            return true;
        }

        MasterVolumeState? target = MasterVolumePlan.Raise(current, ringVolume);
        if (target is null)
        {
            return true;
        }

        // Saved first: a crash after the change must still be undone.
        Save(new Raise(current, target));
        if (!_volume.Apply(target.DeviceId, target.Level, target.Muted))
        {
            // Maybe half applied: put back what was there.
            _volume.Apply(current.DeviceId, current.Level, current.Muted);
            DeleteSaved();
            return false;
        }

        // What the device really holds now (coarse steps round the level): that is what "as the raise left it" means.
        MasterVolumeState? actual = _volume.Read(target.DeviceId);
        if (actual is null)
        {
            _volume.Apply(current.DeviceId, current.Level, current.Muted);
            DeleteSaved();
            return false;
        }

        _held = new Raise(current, actual);
        Save(_held);
        return true;
    }

    private void PutBack(Raise raise)
    {
        // By id, not the default output: the raised one may have stopped being the default.
        if (MasterVolumePlan.ShouldRestore(raise.Raised, _volume.Read(raise.Raised.DeviceId)))
        {
            _volume.Apply(raise.Original.DeviceId, raise.Original.Level, raise.Original.Muted);
        }
    }

    private void Save(Raise raise)
    {
        Task.Run(() => _store.SaveAsync(DocumentKey, raise)).GetAwaiter().GetResult();
    }

    private void DeleteSaved()
    {
        try
        {
            Task.Run(() => _store.DeleteAsync(DocumentKey)).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // A saved restore left behind is checked against the output again at the next start, so it can't do harm.
        }
    }
}
