namespace AccountabiliBuddy.Core.Tasks;

/// <summary>Whether a name can be a label's (<see cref="TaskLabelEdits.CheckName"/>).</summary>
public enum TaskLabelNameCheck
{
    /// <summary>It can.</summary>
    Fine,

    /// <summary>Nothing but spaces: a label needs a name.</summary>
    Blank,

    /// <summary>Another live label has it already (any case).</summary>
    Taken,
}
