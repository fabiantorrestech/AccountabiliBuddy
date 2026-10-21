using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>
/// This PC's copy of another device's part (ADR 22): every file received from it merged into one
/// (<see cref="SyncMerge"/>).
/// </summary>
/// <param name="ReceivedAt">When the newest file from it arrived (the app's trusted time).</param>
/// <param name="Method">How the newest file came: <see cref="SyncDeviceCard.MethodLink"/>, <see cref="SyncDeviceCard.MethodFolder"/>, <see cref="SyncDeviceCard.MethodQr"/> or <see cref="SyncDeviceCard.MethodFile"/>.</param>
/// <param name="Sealed">
/// The newest file's sealed bytes exactly as they arrived: what this PC passes on to other devices. A merged copy is
/// never re-sealed and relayed, since it has gaps where its files didn't meet.
/// </param>
public sealed record ReceivedDevice(SyncFile File, DateTimeOffset ReceivedAt, string Method, byte[] Sealed);
