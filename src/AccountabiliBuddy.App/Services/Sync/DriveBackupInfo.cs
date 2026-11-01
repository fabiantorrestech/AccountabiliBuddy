namespace AccountabiliBuddy.App.Services.Sync;

/// <summary>A backup of a removed device in the app's Drive folder (ADR 55): <c>Backups/&lt;device name&gt;/&lt;date&gt;.absync</c>.</summary>
/// <param name="Id">Its Drive id.</param>
/// <param name="DeviceName">The name of the folder it is in: the removed device's name.</param>
/// <param name="FileName">E.g. "2026-10-28.absync".</param>
public sealed record DriveBackupInfo(string Id, string DeviceName, string FileName, DateTimeOffset ModifiedAt, long Size);
