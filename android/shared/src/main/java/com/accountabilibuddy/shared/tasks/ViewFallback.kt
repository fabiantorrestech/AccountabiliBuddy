package com.accountabilibuddy.shared.tasks

/**
 * ADR 89 (LB1c): a view that filters ONLY by labels or tags this phone doesn't show would open as an empty page. It opens as the Default view
 * instead, with the note "Uses a label from <device>" on the page. Display only: the saved view and the phone's remembered one are unchanged.
 */
object ViewFallback {
    /** [config] is what the page builds from; [note] is shown on the page (null: none). */
    class Resolved(val config: TaskViewConfig, val note: String?, val fellBack: Boolean)

    fun resolve(config: TaskViewConfig, view: TaskScopeView, default: TaskViewConfig, nameOf: (String) -> String): Resolved {
        val note = view.viewNote(config.labels, config.tags, nameOf) ?: return Resolved(config, null, false)
        val named = config.labels.orEmpty().isNotEmpty() || config.tags.orEmpty().isNotEmpty()
        val onlyByLabels = config.only == TaskViewFilter.NONE && !config.noLabel
        val nothingShown = view.shownLabelCount(config.labels) == 0 && view.shownTagCount(config.tags) == 0
        return if (named && onlyByLabels && nothingShown) Resolved(default, note, true) else Resolved(config, null, false)
    }
}
