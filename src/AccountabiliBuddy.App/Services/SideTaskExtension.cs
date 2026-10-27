using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A side task the user gave 5 more minutes.</summary>
/// <param name="State">The session as it is after the extension.</param>
/// <param name="Reason">What the user typed for it (at least 10 characters); sealed in the activity log, never sent.</param>
public sealed record SideTaskExtension(SessionState State, string Reason);
