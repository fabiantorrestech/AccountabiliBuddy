namespace AccountabiliBuddy.Core.Tasks;

/// <summary>A step of an Archive task, as the Archive shows it (after the settle records are applied).</summary>
public sealed record ArchiveStepRow(Guid Id, string Title, DateTimeOffset? DoneAt);
