using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// Notices when time is up (focus ends, a break ends, or the session runs out on its own) and plays the chosen
/// sound, whether or not the on-screen window shows. Ending a session by hand plays nothing. A phase that ran out
/// long ago (the app was closed or the PC asleep, and the session caught up) plays nothing either.
/// Use from the UI thread only.
/// </summary>
public sealed class SessionCueService
{
    private static readonly TimeSpan MaxLateness = TimeSpan.FromMinutes(1);

    private readonly SessionController _sessions;
    private readonly SessionSoundSettingsService _settings;
    private readonly ISoundPlayer _player;

    private SessionState _last = SessionState.Idle;

    public SessionCueService(SessionController sessions, SessionSoundSettingsService settings, ISoundPlayer player)
    {
        _sessions = sessions;
        _settings = settings;
        _player = player;
    }

    /// <summary>Raised when time is up; true = the session ended with it.</summary>
    public event EventHandler<(SessionCue Cue, bool SessionEnded)>? Cued;

    public void Start()
    {
        _last = _sessions.State;
        _sessions.StateChanged += (_, state) => OnStateChanged(state);
        _sessions.SessionCompleted += (_, finished) =>
        {
            if (IsOnTime(finished))
            {
                Raise(finished.IsEnforcing ? SessionCue.FocusEnded : SessionCue.BreakEnded, sessionEnded: true);
            }
        };
    }

    /// <summary>Planning time is up (the "Ready?" card follows): the focus-end chime, and the overlay glows. The session goes on.</summary>
    public void RaisePlanningUp()
    {
        Raise(SessionCue.PlanningEnded, sessionEnded: false);
    }

    /// <summary>Plays a sound by its <see cref="SoundChoice"/> id (also the Test buttons in Settings).</summary>
    /// <param name="volume">0–100 for the built-in chimes; null = the saved volume.</param>
    public void Play(string soundId, int? volume = null)
    {
        int level = volume ?? _settings.Current.Volume;
        switch (soundId)
        {
            case SoundChoice.SoftChimeId:
                _player.PlayWave(ChimeSynth.SoftFalling(level));
                break;
            case SoundChoice.RisingChimeId:
                _player.PlayWave(ChimeSynth.SoftRising(level));
                break;
            case SoundChoice.BellId:
                _player.PlayWave(ChimeSynth.Bell(level));
                break;
            default:
                if (soundId.StartsWith(SoundChoice.WindowsPrefix, StringComparison.Ordinal))
                {
                    string name = Path.GetFileName(soundId[SoundChoice.WindowsPrefix.Length..]);
                    _player.PlayFile(Path.Combine(SoundChoice.WindowsMediaFolder, name));
                }
                else if (soundId.StartsWith(SoundChoice.FilePrefix, StringComparison.Ordinal))
                {
                    _player.PlayFile(soundId[SoundChoice.FilePrefix.Length..]);
                }

                // "off" and unknown ids play nothing.
                break;
        }
    }

    private void OnStateChanged(SessionState state)
    {
        SessionState last = _last;
        _last = state;

        // Only a phase change inside the same running session; starts and ends are handled elsewhere.
        if (!last.IsActive || !state.IsActive || last.Mode != state.Mode || last.Phase == state.Phase || !IsOnTime(last))
        {
            return;
        }

        if (last.IsEnforcing && !state.IsEnforcing)
        {
            Raise(SessionCue.FocusEnded, sessionEnded: false);
        }
        else if (!last.IsEnforcing && state.IsEnforcing)
        {
            Raise(SessionCue.BreakEnded, sessionEnded: false);
        }
    }

    /// <summary>True when the phase that just ended was due within the last minute.</summary>
    private bool IsOnTime(SessionState ended)
    {
        return ended.PhaseEndsAt is DateTimeOffset end && (_sessions.Now - end).Duration() <= MaxLateness;
    }

    private void Raise(SessionCue cue, bool sessionEnded)
    {
        SessionSoundSettings settings = _settings.Current;
        Play(cue == SessionCue.BreakEnded ? settings.BreakEndSound : settings.FocusEndSound);
        Cued?.Invoke(this, (cue, sessionEnded));
    }
}
