namespace AccountabiliBuddy.Core.Sync;

/// <summary>One step in a <see cref="SyncTaskRequest"/>: an existing step's id keeps it (and whether it is ticked); no id, or an id this checklist doesn't know, is a new step.</summary>
public sealed record SyncTaskStep(Guid? Id, string Title);
