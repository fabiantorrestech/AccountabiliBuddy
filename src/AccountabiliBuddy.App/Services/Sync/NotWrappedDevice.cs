namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A device that would be cut off by a removal because it can't receive the new key (ADR 54): see <see cref="SyncRemovalService.NotWrappedFor"/>.</summary>
/// <param name="Name">Its name; a device only known from the others' "seen" lists has no name here.</param>
public sealed record NotWrappedDevice(Guid DeviceId, string Name);
