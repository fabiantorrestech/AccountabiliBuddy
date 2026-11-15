package com.accountabilibuddy.companion.ui.tasks.page

/** Whether the Add a task sheet holds something to lose (FX-A): a typed title, step, note, date or limit, or a picked label, tag or reminder rule. Pure (checked on its own). */
object AddTaskDirty {
    fun isDirty(
        title: String, step: String, steps: List<String>, note: String,
        hasDue: Boolean, hasLimit: Boolean, labelPicked: Boolean, tagsPicked: Boolean, hasRules: Boolean, keepTagsHere: Boolean,
    ): Boolean = title.isNotBlank() || step.isNotBlank() || steps.isNotEmpty() || note.isNotBlank() || hasDue || hasLimit || labelPicked || tagsPicked || hasRules || keepTagsHere
}
