using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>One of tomorrow's day parts a task can be put in on the routine's planning step.</summary>
/// <param name="Label">E.g. "9:00 AM · Deep work".</param>
public sealed record ShutdownPartChoice(string Label, PlannedSlot Slot);
