using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Remote;

/// <summary>
/// The PC's noise player as a phone (and through it the watch) sees and controls it through the sync files (ADR 42, 36):
/// the same buttons the PC's own player has, by the same rules. The headphone lock stays respected (Play while locked
/// is refused), and noise is allowed during a focus session, since it never weakens enforcement. Also says whether the
/// sync poll should run at its fast rate for noise: while the player plays, and for 10 minutes after a command, so the
/// watch's next command doesn't wait for the idle poll. UI thread only.
/// </summary>
public sealed class RemoteNoiseControl
{
    /// <summary>How long after a command the sync poll stays fast.</summary>
    public static readonly TimeSpan FastAfterCommand = TimeSpan.FromMinutes(10);

    private readonly NoisePlayerService _noise;
    private readonly TimeProvider _time;
    private DateTimeOffset? _lastCommandAt;

    // What the last file said (without its time), to tell a change a phone would see.
    private SyncNoise? _shown;

    public RemoteNoiseControl(NoisePlayerService noise, TimeProvider time)
    {
        _noise = noise;
        _time = time;
        _noise.Changed += (_, _) => _ = CheckShownAsync();
    }

    /// <summary>Raised on the UI thread when what a phone sees of the player changed (playing, name, volume, lock, or no sounds).</summary>
    public event EventHandler? StateChanged;

    /// <summary>True while the sync poll should be fast for noise: playing, or a command applied in the last 10 minutes.</summary>
    public bool WantsFastPoll => _noise.IsPlaying || (_lastCommandAt is DateTimeOffset at && _time.GetUtcNow() - at < FastAfterCommand);

    /// <summary>True when the request is one this PC knows (a volume needs a value from 0 to 1).</summary>
    public static bool IsValid(SyncNoiseRequest request) => request.Op switch
    {
        SyncNoiseRequest.OpToggle or SyncNoiseRequest.OpNext or SyncNoiseRequest.OpPrev => true,
        SyncNoiseRequest.OpVolume => request.Value is double value && value >= 0 && value <= 1,
        _ => false,
    };

    /// <summary>The player for this PC's file; null when the folder has no sound files.</summary>
    public async Task<SyncNoise?> StateAsync()
    {
        if (!_noise.HasFiles)
        {
            return null;
        }

        bool locked = await _noise.IsPlayBlockedByLockAsync();
        return new SyncNoise(_noise.IsPlaying, _noise.CurrentName, _noise.Settings.Volume / 100.0, locked, _time.GetUtcNow());
    }

    private async Task CheckShownAsync()
    {
        SyncNoise? state = await StateAsync();
        SyncNoise? now = state is null ? null : state with { At = default };
        if (now != _shown)
        {
            _shown = now;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Does what the PC's own buttons do. <paramref name="request"/> must pass <see cref="IsValid"/>.</summary>
    /// <returns>Null when done; otherwise why not.</returns>
    public async Task<string?> ApplyAsync(SyncNoiseRequest request, string pcName)
    {
        // A refused command counts too: the next one is probably coming.
        _lastCommandAt = _time.GetUtcNow();
        if (!_noise.HasFiles)
        {
            return $"{pcName} has no noise sounds to play.";
        }

        switch (request.Op)
        {
            case SyncNoiseRequest.OpToggle:
                // Pausing is always allowed; playing only where the lock allows it.
                if (!_noise.IsPlaying && await _noise.IsPlayBlockedByLockAsync())
                {
                    return $"Connect headphones on {pcName}";
                }

                _noise.TogglePlay();
                break;
            case SyncNoiseRequest.OpNext:
                _noise.Next();
                break;
            case SyncNoiseRequest.OpPrev:
                _noise.Previous();
                break;
            case SyncNoiseRequest.OpVolume:
                _noise.SetVolume((int)Math.Round(request.Value!.Value * 100));
                break;
        }

        return null;
    }
}
