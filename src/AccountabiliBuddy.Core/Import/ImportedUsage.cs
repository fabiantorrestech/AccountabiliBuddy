namespace AccountabiliBuddy.Core.Import;

/// <summary>Time spent on one site or app, read from another blocker's statistics.</summary>
/// <param name="Name">A site's host (e.g. "youtube.com") or an app's process name (e.g. "Discord").</param>
public sealed record ImportedUsage(string Name, bool IsApp, TimeSpan Total);
