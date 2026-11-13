namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What <see cref="ArchiveBuilder"/> makes: the Archive's tasks (newest first) and its synced Recently deleted lines (newest first).</summary>
public sealed record ArchiveRows(IReadOnlyList<ArchiveTaskRow> Tasks, IReadOnlyList<ArchiveDeletedRow> Deleted);
