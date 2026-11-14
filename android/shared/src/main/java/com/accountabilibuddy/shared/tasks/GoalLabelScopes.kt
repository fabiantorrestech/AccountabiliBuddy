package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.goals.GoalLabelId
import java.util.UUID

/**
 * ADR 89 (LB1c): the label names a goal's "Focus on a label" source offers and names. A name another device keeps to itself (a device-only
 * label, or a device-only tag with that word) is never offered here and never named; a source that already points at one reads
 * "Uses a label from <device>" and keeps counting (counting is data, not display).
 */
object GoalLabelScopes {
    /** [names] (every label word on the phone's lists) that this phone shows: the picker's list. */
    fun pickable(names: List<String>, view: TaskScopeView): List<String> = names.filter { shown(it, view) }

    /** The note for a source on the label [labelId] when it is hidden here ("Uses a label from Work PC", the device's name only); null when it is shown or unknown. */
    fun hiddenNote(labelId: UUID, names: List<String>, view: TaskScopeView, nameOf: (String) -> String): String? {
        val hidden = names.firstOrNull { GoalLabelId.idFor(it) == labelId && !shown(it, view) } ?: return null
        val devices = (view.hiddenNameDevices(hidden, nameOf).orEmpty() + view.hiddenTagDevices(hidden, nameOf).orEmpty()).distinct()
        return if (devices.isEmpty()) null else "Uses a label from " + devices.joinToString(", ")
    }

    private fun shown(name: String, view: TaskScopeView): Boolean = view.tagShown(name) && view.categoryShown(name)
}
