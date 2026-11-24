namespace AccountabiliBuddy.App.Services.Tasks;

/// <summary>
/// Settings › Tasks › Label dot (and Tasks › Options, later): where a task row shows its label (Tasks redesign, checklist 3.74-3.76; plan
/// conflict #8). Stored as its number in "tasks.settings", per mode: the numbers of the older choices are kept so a stored value reads the same.
/// </summary>
public enum TaskLabelStyle
{
    /// <summary>
    /// "Under the task" (the default): a 7 px dot and the label's name at the start of the ↳ sub-line. Was "Dot" (the dot and name at the row's
    /// right), which reads as this.
    /// </summary>
    UnderTask = 0,

    /// <summary>"Pill before the title": a tinted pill with the label's name before the title. Offered in Settings › Tasks only, never in Options.</summary>
    PillBefore = 1,

    /// <summary>
    /// The old "Pill in details" (a pill at the start of the detail line). No longer offered: a stored one reads as <see cref="UnderTask"/>
    /// (<see cref="TaskLabelStyles.Migrated"/>) and is written back as that on the next save.
    /// </summary>
    LegacyPillInDetails = 2,

    /// <summary>"By the check box": an 8 px dot left of the check box; the label's name stays on the sub-line, without a dot.</summary>
    ByCheckBox = 3,
}
