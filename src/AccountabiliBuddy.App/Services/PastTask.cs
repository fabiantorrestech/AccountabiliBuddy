using AccountabiliBuddy.Core.Domain;

namespace AccountabiliBuddy.App.Services;

/// <summary>A task of an ended session's checklist, with when that session started.</summary>
public sealed record PastTask(SessionTask Task, DateTimeOffset SessionStart);
