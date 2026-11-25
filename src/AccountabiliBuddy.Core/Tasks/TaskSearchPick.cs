namespace AccountabiliBuddy.Core.Tasks;

/// <summary>A label or tag chip of Search filters (Tasks redesign 7.21): a click cycles any → ✓ include → ✕ leave out → any.</summary>
public enum TaskSearchPick
{
    /// <summary>The chip narrows nothing.</summary>
    Any,

    /// <summary>✓: only tasks with it (labels: any of the ✓ ones; tags: every ✓ one, as typing "#tag" does).</summary>
    Include,

    /// <summary>✕: tasks with it are left out.</summary>
    LeaveOut,
}
