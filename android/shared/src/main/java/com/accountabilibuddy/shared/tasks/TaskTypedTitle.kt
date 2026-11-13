package com.accountabilibuddy.shared.tasks

/**
 * What the Add sheet makes of typed text (the PC's `TaskTypedTitle`): the [title] without its "@label" and "#tag" words, the label
 * ([labelId]; null for none) and the [tags] (private text).
 */
data class TaskTypedTitle(val title: String, val labelId: String?, val tags: List<String>)
