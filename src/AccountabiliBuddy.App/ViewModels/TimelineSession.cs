using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A focus session you started, as the Focus row shows it (the running one up to now).</summary>
/// <param name="ProfileId">Its profile; clicking the session opens it.</param>
public sealed record TimelineSession(LocalWindow Window, string Tip, Guid? ProfileId);
