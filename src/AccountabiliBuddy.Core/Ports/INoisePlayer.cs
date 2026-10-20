namespace AccountabiliBuddy.Core.Ports;

/// <summary>
/// Plays sound files one after another (the noise player: white noise, rain…), with its own volume, apart from the
/// system's. <see cref="StateChanged"/> may be raised on any thread.
/// </summary>
public interface INoisePlayer : IDisposable
{
    /// <summary>Raised when playing starts or stops, or another file starts.</summary>
    event EventHandler? StateChanged;

    bool IsPlaying { get; }

    /// <summary>The index, in the loaded list, of the file playing or paused; -1 when nothing is loaded.</summary>
    int CurrentIndex { get; }

    /// <summary>At the end of the list, start over from the first file.</summary>
    bool Repeat { get; set; }

    /// <summary>0–1.</summary>
    double Volume { get; set; }

    /// <summary>Whether the keyboard's media keys and the system's media controls reach this player.</summary>
    bool UsesMediaKeys { get; set; }

    /// <summary>Replaces the list (full paths) and doesn't start playing.</summary>
    void Load(IReadOnlyList<string> files, int startIndex);

    void Play();

    void Pause();

    void Next();

    /// <summary>Back to the start of the file, or to the previous file when this one has only just started.</summary>
    void Previous();
}
