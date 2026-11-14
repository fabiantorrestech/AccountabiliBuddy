namespace AccountabiliBuddy.Core.Tasks;

/// <summary>
/// What <see cref="ArchiveBuilder"/> makes: the Archive's tasks (newest first), its synced Recently deleted lines (newest first) and
/// its session notes (newest first, AR1b).
/// </summary>
public sealed record ArchiveRows(IReadOnlyList<ArchiveTaskRow> Tasks, IReadOnlyList<ArchiveDeletedRow> Deleted)
{
    /// <summary>Every device's live session notes, newest first (ADR 87).</summary>
    public IReadOnlyList<ArchiveNoteRow> Notes { get; init; } = [];
}
