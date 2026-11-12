using AccountabiliBuddy.Core.Tasks;

namespace AccountabiliBuddy.App.ViewModels;

/// <summary>
/// One of This session's places in the "This session is full" dialog: a task, or a focused step ("142b · Fix tray icon blur", sub
/// "Step of Electron App…"). Swapping it out sends it to Queued (a step only leaves focus while its task keeps another).
/// </summary>
/// <param name="Number">Its number in This session.</param>
/// <param name="Slot">The running session's place; null before a start (the draft's task).</param>
/// <param name="TaskId">The task it belongs to.</param>
/// <param name="Title">What the row reads; shown only.</param>
/// <param name="Sub">"Step of …" for a step; empty otherwise.</param>
/// <param name="LabelColor">The label's colour (#RRGGBB); null for none.</param>
public sealed record SessionSlotChoice(int Number, SessionSlot? Slot, Guid TaskId, string Title, string Sub, string? LabelColor);
