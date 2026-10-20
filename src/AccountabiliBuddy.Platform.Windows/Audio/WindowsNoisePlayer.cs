using AccountabiliBuddy.Core.Ports;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace AccountabiliBuddy.Platform.Windows.Audio;

/// <summary>
/// The noise player on Windows' own media player (mp3, wav, m4a/aac, flac, wma). Files are streamed from disk, so a
/// 10-hour file costs no memory. Its volume is its own; Windows' volume and the chimes are untouched. It stays out of
/// the media keys and Windows' media pop-up unless <see cref="UsesMediaKeys"/> is on.
/// </summary>
public sealed class WindowsNoisePlayer : INoisePlayer
{
    // Pressing Previous later than this into a file goes back to its start instead of the file before.
    private static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private readonly MediaPlayer _player;
    private readonly List<MediaSource> _sources = new();
    private MediaPlaybackList? _list;
    private bool _repeat = true;

    // Play was asked for and not undone by Pause or the end of the list; the player reports Opening/Buffering on the way.
    private bool _wantsToPlay;

    public WindowsNoisePlayer()
    {
        _player = new MediaPlayer { AutoPlay = false };
        _player.CommandManager.IsEnabled = false;
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        _player.MediaEnded += (_, _) =>
        {
            // Only reached at the end of the list with repeat off.
            _wantsToPlay = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? StateChanged;

    public bool IsPlaying => _player.PlaybackSession.PlaybackState switch
    {
        MediaPlaybackState.Playing => true,
        MediaPlaybackState.Opening or MediaPlaybackState.Buffering => _wantsToPlay,
        _ => false,
    };

    public int CurrentIndex
    {
        get
        {
            if (_list is null || _list.Items.Count == 0)
            {
                return -1;
            }

            uint index = _list.CurrentItemIndex;
            return index < _list.Items.Count ? (int)index : 0;
        }
    }

    public bool Repeat
    {
        get => _repeat;
        set
        {
            _repeat = value;
            if (_list is not null)
            {
                _list.AutoRepeatEnabled = value;
            }
        }
    }

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    public bool UsesMediaKeys
    {
        get => _player.CommandManager.IsEnabled;
        set => _player.CommandManager.IsEnabled = value;
    }

    public void Load(IReadOnlyList<string> files, int startIndex)
    {
        _wantsToPlay = false;
        _player.Pause();

        MediaPlaybackList? list = null;
        var sources = new List<MediaSource>();
        if (files.Count > 0)
        {
            list = new MediaPlaybackList { AutoRepeatEnabled = _repeat };
            foreach (string file in files)
            {
                MediaSource source = MediaSource.CreateFromUri(new Uri(file));
                sources.Add(source);
                list.Items.Add(new MediaPlaybackItem(source));
            }

            list.StartingItem = list.Items[Math.Clamp(startIndex, 0, files.Count - 1)];
            list.CurrentItemChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
        }

        _player.Source = list;
        _list = list;
        foreach (MediaSource old in _sources)
        {
            old.Dispose();
        }

        _sources.Clear();
        _sources.AddRange(sources);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (_list is null)
        {
            return;
        }

        _wantsToPlay = true;
        _player.Play();
    }

    public void Pause()
    {
        _wantsToPlay = false;
        _player.Pause();
    }

    public void Next()
    {
        if (_list is null)
        {
            return;
        }

        bool last = CurrentIndex == _list.Items.Count - 1;
        if (_list.Items.Count == 1 || (last && !_repeat))
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            return;
        }

        _list.MoveNext();
    }

    public void Previous()
    {
        if (_list is null)
        {
            return;
        }

        bool first = CurrentIndex == 0;
        if (_list.Items.Count == 1 || _player.PlaybackSession.Position > RestartThreshold || (first && !_repeat))
        {
            _player.PlaybackSession.Position = TimeSpan.Zero;
            return;
        }

        if (first)
        {
            _list.MoveTo((uint)(_list.Items.Count - 1));
            return;
        }

        _list.MovePrevious();
    }

    public void Dispose()
    {
        _player.Dispose();
        foreach (MediaSource source in _sources)
        {
            source.Dispose();
        }

        _sources.Clear();
    }
}
