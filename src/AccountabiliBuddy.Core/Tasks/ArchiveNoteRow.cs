namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// One session note of the Archive (AR1b, ADR 87), the same on every device: every device's notes, newest edit applied, deleted ones
/// gone. The note's text is the user's private text: shown, never logged.
/// </summary>
/// <param name="By">The device that wrote it (an id only); empty for an old note not yet stamped (this device's own).</param>
/// <param name="DeviceName">The writing device's name when it isn't this one: its last known name, null when none is known; null for this device's own notes.</param>
/// <param name="FromOther">The note was written on another device (a removed device included): the row says "from &lt;device&gt;".</param>
public sealed record ArchiveNoteRow(Guid Id, string Text, DateTimeOffset At, Guid By, string? DeviceName, bool FromOther)
{
    /// <summary>The words after the time: "from Work PC", "from another device" (a removed device, or one never named), or null for this device's own note.</summary>
    public string? FromText => FromOther ? $"from {DeviceName ?? "another device"}" : null;
}
