namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The Google Drive gate of a role change (ADR 90, owner round 2: "a role change applies only once the sync change has been confirmed uploaded
/// to Google Drive"). Pure; the phone's twin is Kotlin <c>SyncDeviceRoleGate</c>. It reads only this device's OWN send log (the PC's
/// <c>SyncSendLog</c>, the phone's <c>SyncSendLog</c>: per method, the write time of each of its own puts that reached the method), never other
/// devices' receipts, which need a peer that reads by that method and a round trip.
/// <list type="bullet">
/// <item>With a Drive method in use, the gate opens when this device's put on Drive holds a file written at or after the change.</item>
/// <item>With no Drive method, a put on the shared folder or the relay counts instead (any of them in use).</item>
/// <item>With none of those in use there is nothing to confirm: <see cref="SyncDeviceRoleGateState.NothingToConfirm"/>.</item>
/// </list>
/// Times compare as instants, never by offset.
/// </summary>
public static class SyncDeviceRoleGate
{
    /// <param name="changeAt">The role entry's time (<see cref="SyncDeviceRole.At"/>).</param>
    /// <param name="newestPutByMethod">Per method (<c>drive</c>, <c>folder</c>, <c>relay</c>), the newest write time among this device's own puts that reached it; a method with no put is absent.</param>
    /// <param name="methodsInUse">The methods this device uses (<see cref="SyncDeviceCard.Methods"/>).</param>
    public static SyncDeviceRoleGateState Evaluate(
        DateTimeOffset changeAt, IReadOnlyDictionary<string, DateTimeOffset> newestPutByMethod, IReadOnlyCollection<string> methodsInUse)
    {
        bool Reached(string method) => newestPutByMethod.TryGetValue(method, out DateTimeOffset writtenAt) && writtenAt >= changeAt;

        if (methodsInUse.Contains(SyncDeviceCard.MethodDrive))
        {
            return Reached(SyncDeviceCard.MethodDrive) ? SyncDeviceRoleGateState.Open : SyncDeviceRoleGateState.Waiting;
        }

        string[] confirmable = [SyncDeviceCard.MethodFolder, SyncDeviceCard.MethodRelay];
        if (!confirmable.Any(methodsInUse.Contains))
        {
            return SyncDeviceRoleGateState.NothingToConfirm;
        }

        return confirmable.Any(method => methodsInUse.Contains(method) && Reached(method)) ? SyncDeviceRoleGateState.Open : SyncDeviceRoleGateState.Waiting;
    }
}
