namespace AccountabiliBuddy.Core.Tasks;

/// <summary>Whether a name can be a label's (<see cref="TaskLabelEdits.CheckName"/>) or a label group's (<see cref="TaskLabelGroupEdits.CheckName"/>, ADR 93).</summary>
public enum TaskLabelNameCheck
{
    /// <summary>It can.</summary>
    Fine,

    /// <summary>Nothing but spaces: a label needs a name.</summary>
    Blank,

    /// <summary>Another live label (or, for a group, another live group) has it already (any case).</summary>
    Taken,
}
