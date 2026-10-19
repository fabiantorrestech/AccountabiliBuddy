namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The sounds played when time is up. App-owned (documents "sounds"), display only. Sound ids are listed in
/// <see cref="SoundChoice"/>.
/// </summary>
/// <param name="FocusEndSound">When focus is up: a break starts, or the session ends after focus.</param>
/// <param name="BreakEndSound">When a break is up: focus starts again, or the session ends after the break.</param>
/// <param name="Volume">0–100, for the built-in chimes (Windows can't change the volume of other sound files).</param>
/// <param name="CustomFile">A .wav file the user added to the choices, if any.</param>
public sealed record SessionSoundSettings(string FocusEndSound, string BreakEndSound, int Volume, string? CustomFile)
{
    public static SessionSoundSettings Default { get; } = new(SoundChoice.SoftChimeId, SoundChoice.RisingChimeId, 60, null);
}
