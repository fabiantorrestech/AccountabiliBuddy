package com.accountabilibuddy.shared.tasks

/** Whether a name can be a label's ([TaskLabelEdits.checkName]; the PC's TaskLabelNameCheck). */
enum class TaskLabelNameCheck {
    /** It can. */
    Fine,

    /** Nothing but spaces: a label needs a name. */
    Blank,

    /** Another live label has it already (any case). */
    Taken,
}
