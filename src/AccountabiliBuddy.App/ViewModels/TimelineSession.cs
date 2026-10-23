using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>A focus session you started, as the Focus row shows it (the running one up to now).</summary>
/// <param name="ProfileId">Its profile; clicking the session opens it.</param>
/// <param name="Added">Added by hand in History (ADR 20): drawn lighter, striped.</param>
/// <param name="Color">"#RRGGBB" for another device's session (ADR 22, its shade); null = the Focus row's own colour.</param>
/// <param name="Away">Stretches the user had stepped away during it: drawn hatched over it, left out of the focused time.</param>
public sealed record TimelineSession(
    LocalWindow Window, string Tip, Guid? ProfileId, bool Added = false, string? Color = null, IReadOnlyList<LocalWindow>? Away = null);
