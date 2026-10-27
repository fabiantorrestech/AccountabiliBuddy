namespace AccountabiliBuddy.Core.Sync;

/// <summary>
/// The <see cref="SyncAction.Text"/> of a <see cref="SyncAction.KindStart"/> action (ADR 42), as JSON:
/// <c>{ "profileId": "…", "minutes": 25 }</c>, <c>{ "profileId": "…", "noSetEnd": true }</c>, or a Quick Start that names no length:
/// <c>{ "profileId": "…" }</c> (it runs for the PC's remembered focus length and counts down).
/// </summary>
/// <param name="ProfileId">The profile to start; with no length (or <paramref name="NoSetEnd"/>) and none, the PC's default profile.</param>
/// <param name="Minutes">The length, 1 to 720; null is a Quick Start; ignored with <paramref name="NoSetEnd"/>.</param>
/// <param name="NoSetEnd">An explicit session with no set end (count-up).</param>
public sealed record SyncStartRequest(Guid? ProfileId, int? Minutes, bool NoSetEnd = false);
