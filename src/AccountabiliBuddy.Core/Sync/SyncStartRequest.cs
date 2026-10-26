namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The <see cref="SyncAction.Text"/> of a <see cref="SyncAction.KindStart"/> action (ADR 42), as JSON:
/// <c>{ "profileId": "…", "minutes": 25 }</c> or <c>{ "profileId": "…", "noSetEnd": true }</c>.
/// </summary>
/// <param name="ProfileId">The profile to start; with <paramref name="NoSetEnd"/> and none, the PC's Quick Start default.</param>
/// <param name="Minutes">The length, 1 to 720; ignored with <paramref name="NoSetEnd"/>.</param>
/// <param name="NoSetEnd">A session with no set end (count-up).</param>
public sealed record SyncStartRequest(Guid? ProfileId, int? Minutes, bool NoSetEnd = false);
