namespace AccountabiliBuddy.Core.Sync;

/// <summary>What a device effectively is once the role list is resolved (<see cref="SyncDeviceRoles.Effective"/>, ADR 90). Android devices only: a PC or a watch has none.</summary>
public enum SyncDeviceRoleKind
{
    /// <summary>The one winning main phone: counts the group's steps and sleep, and feeds the PC's alarm, bedtime and wake estimate.</summary>
    Main,

    /// <summary>A tablet. Counts no steps or sleep. Its own guards stay its own.</summary>
    Tablet,

    /// <summary>Any other Android device: a second phone, a spare. Counts no steps or sleep.</summary>
    Other,

    /// <summary>No role and no main phone in the group: behaves exactly as before roles existed (every Android device is "the phone").</summary>
    Unassigned,
}
