namespace AccountabiliBuddy.App.ViewModels;

/// <summary>How a device stands in the Devices tab (ADR 22): shown in words and colour, never colour alone.</summary>
public enum DeviceSyncState
{
    /// <summary>This PC's own row.</summary>
    ThisDevice,

    /// <summary>Its newest file is no older than its interval plus 5 minutes.</summary>
    UpToDate,

    /// <summary>Older than that.</summary>
    Behind,

    /// <summary>In the group, but nothing from it yet.</summary>
    Waiting,

    /// <summary>This PC isn't in a device group.</summary>
    Unlinked,
}
