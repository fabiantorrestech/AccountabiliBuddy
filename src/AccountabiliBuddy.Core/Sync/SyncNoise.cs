using System.Text.Json.Serialization;

namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// What a PC's noise player is doing, as its file says (ADR 42, 36): <c>{ "playing", "name"?, "volume", "locked", "at" }</c>,
/// so a phone (and through it the watch) can show and control it. Absent when the PC has no sound files to play.
/// </summary>
/// <param name="Playing">The player is playing now.</param>
/// <param name="Name">The shown name of the current file (the user's own label, or the file name without folder or extension); never the path.</param>
/// <param name="Volume">0.0 to 1.0.</param>
/// <param name="Locked">The headphone lock is on and blocks play right now (no headphones are the sound output).</param>
/// <param name="At">When the PC looked, on its clock.</param>
public sealed record SyncNoise(
    bool Playing,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name,
    double Volume,
    bool Locked,
    DateTimeOffset At);
