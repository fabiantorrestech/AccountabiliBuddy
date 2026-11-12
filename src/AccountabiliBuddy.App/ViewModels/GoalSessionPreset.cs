namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// A session preset the goal menu's "Start a session ›" offers inline (round 2 R5.4): a focus profile the user can start now. The default
/// is the Quick Start default profile (no goal keeps a preset of its own yet, Q426).
/// </summary>
public sealed record GoalSessionPreset(Guid ProfileId, string Name, bool IsDefault);
