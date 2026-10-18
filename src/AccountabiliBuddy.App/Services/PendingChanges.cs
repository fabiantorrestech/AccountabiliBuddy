using AccountabiliBuddy.Core.Storage;

namespace AccountabiliBuddy.App.Services;

/// <summary>A saved configuration whose looser parts are still counting down.</summary>
/// <param name="Document">The configuration as the user last saved it; it replaces the current one at <paramref name="ReadyAt"/>.</param>
/// <param name="ReadyAt">When it takes effect, unless a session is running then (it waits for the session to end).</param>
/// <param name="Changes">The looser changes it contains, in plain words.</param>
public sealed record PendingChanges(ProfileDocument Document, DateTimeOffset ReadyAt, IReadOnlyList<string> Changes);
