using AccountabiliBuddy.Core.Domain;
using AccountabiliBuddy.Core.Ports;
using Avalonia.Threading;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The noise player in the sidebar and on the on-screen window: plays the sound files of one folder in name order,
/// repeats by default, with its own volume, and never starts by itself. The player is only created on the first
/// Play, so it costs nothing until then. Its shortcuts (play/pause, volume ±10%) work anywhere. Use from the UI thread;
/// <see cref="Changed"/> is raised there.
/// </summary>
public sealed class NoisePlayerService : IDisposable
{
    public static IReadOnlyList<string> Extensions { get; } = new[] { ".mp3", ".wav", ".m4a", ".aac", ".flac", ".wma" };

    private const int VolumeStep = 10;

    private readonly Func<INoisePlayer> _createPlayer;
    private readonly NoiseSettingsService _settings;
    private readonly IGlobalHotkey _playPauseKey;
    private readonly IGlobalHotkey _volumeUpKey;
    private readonly IGlobalHotkey _volumeDownKey;
    private INoisePlayer? _player;
    private IReadOnlyList<string> _files = Array.Empty<string>();

    // Before the player exists, Previous/Next only move this.
    private int _index;

    /// <param name="createPlayer">Makes the player; called on the first Play.</param>
    /// <param name="createHotkey">Makes one global shortcut (one per action).</param>
    public NoisePlayerService(Func<INoisePlayer> createPlayer, NoiseSettingsService settings, Func<IGlobalHotkey> createHotkey)
    {
        _createPlayer = createPlayer;
        _settings = settings;
        _playPauseKey = createHotkey();
        _volumeUpKey = createHotkey();
        _volumeDownKey = createHotkey();
        _playPauseKey.Pressed += (_, _) => Dispatcher.UIThread.Post(TogglePlay);
        _volumeUpKey.Pressed += (_, _) => Dispatcher.UIThread.Post(() => StepVolume(+1));
        _volumeDownKey.Pressed += (_, _) => Dispatcher.UIThread.Post(() => StepVolume(-1));
    }

    public event EventHandler? Changed;

    public NoiseSettings Settings => _settings.Current;

    public bool HasFiles => _files.Count > 0;

    public int FileCount => _files.Count;

    public bool IsPlaying => _player?.IsPlaying == true;

    /// <summary>The file playing or next to play: its shown name, or its file name without the extension; null when the folder has none.</summary>
    public string? CurrentName
    {
        get
        {
            if (!HasFiles)
            {
                return null;
            }

            string file = Path.GetFileName(_files[CurrentIndex]);
            return _settings.Current.DisplayNames?.GetValueOrDefault(file) ?? Path.GetFileNameWithoutExtension(file);
        }
    }

    /// <summary>Why a shortcut couldn't be set up (taken by another app); null when all are fine.</summary>
    public string? ShortcutError { get; private set; }

    private int CurrentIndex => _player is { CurrentIndex: >= 0 } player && player.CurrentIndex < _files.Count
        ? player.CurrentIndex
        : Math.Clamp(_index, 0, Math.Max(0, _files.Count - 1));

    public void Start()
    {
        LoadFolder();
        ShortcutError = RegisterShortcuts(_settings.Current);
    }

    public void TogglePlay()
    {
        if (IsPlaying)
        {
            _player!.Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Plays the current file if it isn't playing (focus headphones connected).</summary>
    public void PlayNow()
    {
        if (!IsPlaying)
        {
            Play();
        }
    }

    /// <summary>Pauses if playing (focus headphones went away).</summary>
    public void PauseNow()
    {
        if (IsPlaying)
        {
            _player!.Pause();
        }
    }

    public void Next()
    {
        if (_player is null)
        {
            _index = HasFiles ? (_index + 1) % _files.Count : 0;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _player.Next();
    }

    public void Previous()
    {
        if (_player is null)
        {
            _index = HasFiles ? (_index - 1 + _files.Count) % _files.Count : 0;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _player.Previous();
    }

    /// <summary>Shows the current file under another name (the file itself keeps its name); empty = its file name again.</summary>
    public async Task RenameCurrentAsync(string? name)
    {
        if (!HasFiles)
        {
            return;
        }

        string file = Path.GetFileName(_files[CurrentIndex]);
        var names = new Dictionary<string, string>(_settings.Current.DisplayNames ?? new Dictionary<string, string>());
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == Path.GetFileNameWithoutExtension(file))
        {
            names.Remove(file);
        }
        else
        {
            names[file] = name.Trim();
        }

        await _settings.SaveAsync(_settings.Current with { DisplayNames = names });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetVolume(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (_player is not null)
        {
            _player.Volume = percent / 100.0;
        }

        if (percent != _settings.Current.Volume)
        {
            _settings.SaveSoon(_settings.Current with { Volume = percent });
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Up or down to the next multiple of 10%.</summary>
    public void StepVolume(int direction)
    {
        int current = _settings.Current.Volume;
        int stepped = direction > 0
            ? (current / VolumeStep + 1) * VolumeStep
            : ((current + VolumeStep - 1) / VolumeStep - 1) * VolumeStep;
        SetVolume(stepped);
    }

    public void SetRepeat(bool repeat)
    {
        if (_player is not null)
        {
            _player.Repeat = repeat;
        }

        if (repeat != _settings.Current.Repeat)
        {
            _settings.SaveSoon(_settings.Current with { Repeat = repeat });
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Plays from another folder (stopped until Play), or Music\Noise again with null.</summary>
    public async Task ChooseFolderAsync(string? folder)
    {
        await _settings.SaveAsync(_settings.Current with { Folder = folder, LastFile = null });
        LoadFolder();
    }

    /// <summary>Picks up files added to or removed from the folder; stops playing.</summary>
    public void Rescan()
    {
        LoadFolder();
    }

    /// <summary>Saves the media-key choice and the shortcuts, then sets them up.</summary>
    /// <returns>Null when done; otherwise what's wrong (a shortcut that isn't valid isn't saved; one taken by another app is).</returns>
    public async Task<string?> SaveKeysAsync(bool usesMediaKeys, string playPause, string volumeUp, string volumeDown)
    {
        foreach (string text in new[] { playPause, volumeUp, volumeDown }.Where(text => text.Trim().Length > 0))
        {
            if (!HotkeyChord.TryParse(text, out _, out string? error))
            {
                return $"Noise shortcut \"{text}\": {error}";
            }
        }

        await _settings.SaveAsync(_settings.Current with
        {
            UsesMediaKeys = usesMediaKeys,
            PlayPauseKey = playPause.Trim(),
            VolumeUpKey = volumeUp.Trim(),
            VolumeDownKey = volumeDown.Trim(),
        });
        if (_player is not null)
        {
            _player.UsesMediaKeys = usesMediaKeys;
        }

        ShortcutError = RegisterShortcuts(_settings.Current);
        Changed?.Invoke(this, EventArgs.Empty);
        return ShortcutError;
    }

    public void Dispose()
    {
        foreach (IGlobalHotkey hotkey in new[] { _playPauseKey, _volumeUpKey, _volumeDownKey })
        {
            hotkey.Unregister();
            (hotkey as IDisposable)?.Dispose();
        }

        _player?.Dispose();
        _player = null;
    }

    private void Play()
    {
        if (!HasFiles)
        {
            return;
        }

        if (_player is null)
        {
            NoiseSettings settings = _settings.Current;
            _player = _createPlayer();
            _player.Volume = settings.Volume / 100.0;
            _player.Repeat = settings.Repeat;
            _player.UsesMediaKeys = settings.UsesMediaKeys;
            _player.StateChanged += (_, _) => Dispatcher.UIThread.Post(OnPlayerChanged);
            _player.Load(_files, _index);
        }

        _player.Play();
    }

    private void OnPlayerChanged()
    {
        if (_player is null)
        {
            return;
        }

        string? name = HasFiles ? Path.GetFileName(_files[CurrentIndex]) : null;
        if (name is not null && name != _settings.Current.LastFile)
        {
            _settings.SaveSoon(_settings.Current with { LastFile = name });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFolder()
    {
        string folder = _settings.Current.EffectiveFolder;
        _files = Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<string>();

        string? last = _settings.Current.LastFile;
        int found = last is null ? -1 : _files.ToList().FindIndex(file => Path.GetFileName(file) == last);
        _index = Math.Max(0, found);
        _player?.Load(_files, _index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string? RegisterShortcuts(NoiseSettings settings)
    {
        var errors = new List<string>();
        Register(_playPauseKey, settings.PlayPauseKey, "play/pause", errors);
        Register(_volumeUpKey, settings.VolumeUpKey, "volume up", errors);
        Register(_volumeDownKey, settings.VolumeDownKey, "volume down", errors);
        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    private static void Register(IGlobalHotkey hotkey, string text, string label, List<string> errors)
    {
        if (text.Trim().Length == 0)
        {
            hotkey.Unregister();
            return;
        }

        if (!HotkeyChord.TryParse(text, out HotkeyChord? chord, out string? error) || !hotkey.TryRegister(chord, out error))
        {
            errors.Add($"Noise {label}: {error}");
        }
    }
}
