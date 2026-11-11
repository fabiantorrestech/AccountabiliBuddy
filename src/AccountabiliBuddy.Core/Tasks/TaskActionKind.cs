namespace AccountabiliBuddy.Core.Tasks;

/// <summary>What a task action did (ADR 76), as the undo stack records it.</summary>
public enum TaskActionKind
{
    Move,
    Finish,
    Delete,
    Edit,
    Bulk,
    History,
}
