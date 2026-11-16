namespace AccountabiliBuddy.Core.Sync;

/// <summary>Where a role change stands against the Google Drive gate (ADR 90, <see cref="SyncDeviceRoleGate"/>).</summary>
public enum SyncDeviceRoleGateState
{
    /// <summary>This device's own upload record doesn't yet hold a file written at or after the change: "Waiting to reach Google Drive...".</summary>
    Waiting,

    /// <summary>A file written at or after the change has been put on the method the gate reads: "On Google Drive".</summary>
    Open,

    /// <summary>This device uses no method that records a confirmed put (QR, file import, no transport): nothing to confirm, only the usual wait.</summary>
    NothingToConfirm,
}
