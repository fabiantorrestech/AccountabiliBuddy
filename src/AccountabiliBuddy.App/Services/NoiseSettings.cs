namespace AccountabiliBuddy.App.Services;

/// <summary>The noise player's settings. App-owned (documents "noise").</summary>
/// <param name="Folder">The folder whose sound files play; null = Music\Noise (<see cref="DefaultFolder"/>).</param>
/// <param name="Volume">0–100, the player's own volume.</param>
/// <param name="Repeat">Start over from the first file at the end of the folder.</param>
/// <param name="LastFile">The file name last playing, so it picks up there.</param>
/// <param name="UsesMediaKeys">The keyboard's media keys and Windows' media pop-up also control the noise.</param>
/// <param name="PlayPauseKey">Global shortcut such as "Ctrl+Alt+P"; empty = none.</param>
/// <param name="VolumeUpKey">Global shortcut for +10% volume; empty = none.</param>
/// <param name="VolumeDownKey">Global shortcut for −10% volume; empty = none.</param>
/// <param name="DisplayNames">Names shown instead of file names (key: file name); the files keep theirs.</param>
/// <param name="LockedToHeadphones">The player's lock: it doesn't play unless headphones are connected, and pauses when the last ones go.</param>
/// <param name="StopsWhenHeadphonesGo">Pause when any headphones disconnect, locked or not (apart from auto-play's chosen headphones).</param>
public sealed record NoiseSettings(
    string? Folder,
    int Volume,
    bool Repeat,
    string? LastFile,
    bool UsesMediaKeys,
    string PlayPauseKey,
    string VolumeUpKey,
    string VolumeDownKey,
    IReadOnlyDictionary<string, string>? DisplayNames = null,
    bool LockedToHeadphones = false,
    bool StopsWhenHeadphonesGo = false)
{
    public static NoiseSettings Default { get; } = new(null, 40, true, null, false, string.Empty, string.Empty, string.Empty);

    public static string DefaultFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Noise");

    public string EffectiveFolder => Folder ?? DefaultFolder;
}
