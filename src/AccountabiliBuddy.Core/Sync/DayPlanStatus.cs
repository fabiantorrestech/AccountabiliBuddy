namespace AccountabiliBuddy.Core.Sync;

/// <summary>The outcomes of a day plan edit from a device (ADR 19).</summary>
public static class DayPlanStatus
{
    /// <summary>Saved (or already the PC's plan); the settings push brings it back to the device.</summary>
    public const string Applied = "applied";

    /// <summary>A focus session the user started is running; the device keeps the edit and sends it later.</summary>
    public const string Locked = "locked";

    /// <summary>The PC's plan changed since the device's copy; the answer carries it so the user can pick one.</summary>
    public const string Conflict = "conflict";

    /// <summary>The plan breaks the editor's rules or can't be read; nothing changed.</summary>
    public const string Invalid = "invalid";
}
