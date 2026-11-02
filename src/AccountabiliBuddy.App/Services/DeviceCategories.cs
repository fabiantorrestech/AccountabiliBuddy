using AccountabiliBuddy.Core.Sync;

namespace AccountabiliBuddy.App.Services;

/// <summary>
/// The task categories another device of the group lists in its sync file, for the pickers that group them by device. Only devices
/// still in the group, and only ones with at least one category, are listed. Names are the user's text: never logged.
/// </summary>
/// <param name="DeviceId">The device's id.</param>
/// <param name="DeviceName">The device's name as the user knows it, e.g. "Work laptop".</param>
/// <param name="Categories">Its categories in its own order.</param>
public sealed record DeviceCategories(Guid DeviceId, string DeviceName, IReadOnlyList<SyncCategory> Categories);
