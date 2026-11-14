package com.accountabilibuddy.shared.tasks

/**
 * ADR 89 (LB1c): the ask-first step of a scope change that shows or hides a label on the user's other devices ("Make shared", "Only on
 * this phone" for a shared label). A tap only asks ([ask]); only the dialog's confirm acts, and on what was asked about ([confirmed]).
 * Nothing asked, nothing to confirm: a tap alone never changes a scope. The screens keep one of these per dialog.
 */
data class ScopeAsk<T>(val target: T? = null) {
    /** Whether the dialog is open. */
    val asking: Boolean get() = target != null

    fun ask(target: T): ScopeAsk<T> = ScopeAsk(target)

    fun dismissed(): ScopeAsk<T> = ScopeAsk()

    /** What the dialog's confirm acts on; null when nothing was asked. */
    fun confirmed(): T? = target
}
