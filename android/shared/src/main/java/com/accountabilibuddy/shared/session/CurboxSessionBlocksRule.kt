package com.accountabilibuddy.shared.session

/**
 * The Curbox blocks a phone session profile turns on for the session (ADR 85, plan section 3 PS2 (2)). Pure: it is handed what the session wants, what
 * Curbox says now, and what it remembers, and answers what to do and what to remember.
 *
 * - Start (or each refresh while the session runs): a wanted block that is off is turned on. Only a block that was OFF when the session first looked
 *   is "ours"; one that was already on is never turned off by us.
 * - A block we turned on and the user turned off in Curbox is turned on again at the next refresh (plan check S9, stricter); one that was on before the
 *   session and was switched off in Curbox is turned on again but stays "not ours".
 * - End (or emergency time): only the blocks that are ours and still on are turned off. Nothing else.
 * - Curbox not answering (no list): nothing happens and the memory is kept, so the next refresh tries again.
 */
object CurboxSessionBlocksRule {
    /** What this phone remembers (phone-local prefs): [ours] turned on by us, [onBefore] already on when the session started. */
    data class Memory(val ours: Set<CurboxBlockRef> = emptySet(), val onBefore: Set<CurboxBlockRef> = emptySet()) {
        val isEmpty: Boolean get() = ours.isEmpty() && onBefore.isEmpty()
    }

    sealed interface Action {
        val block: CurboxBlockRef
        data class TurnOn(override val block: CurboxBlockRef) : Action
        data class TurnOff(override val block: CurboxBlockRef) : Action
    }

    /** The answer: the actions, and the memory once they all worked (the caller keeps the old memory for an action Curbox refused). */
    data class Step(val actions: List<Action>, val memory: Memory)

    /**
     * @param running The session runs and no emergency time is on.
     * @param wanted The profile's blocks.
     * @param current Curbox's blocks and whether each is on; null = Curbox can't be asked.
     */
    fun step(running: Boolean, wanted: List<CurboxBlockRef>, current: Map<CurboxBlockRef, Boolean>?, memory: Memory): Step {
        if (current == null) return Step(emptyList(), memory)
        return if (running) start(wanted, current, memory) else end(current, memory)
    }

    /**
     * The memory to keep once the actions were tried: a block Curbox did not turn ON is not ours (and is tried again next time); one it did not turn OFF stays
     * ours (turned off next time). [failed] holds the actions that did not work.
     */
    fun settle(step: Step, failed: Collection<Action>): Memory {
        if (failed.isEmpty()) return step.memory
        val failedOn = failed.filterIsInstance<Action.TurnOn>().map { it.block }.toSet()
        val failedOff = failed.filterIsInstance<Action.TurnOff>().map { it.block }.toSet()
        return Memory(ours = step.memory.ours - failedOn + failedOff, onBefore = step.memory.onBefore)
    }

    private fun start(wanted: List<CurboxBlockRef>, current: Map<CurboxBlockRef, Boolean>, memory: Memory): Step {
        val actions = mutableListOf<Action>()
        var ours = memory.ours
        var onBefore = memory.onBefore
        val first = memory.isEmpty
        for (block in wanted.distinct()) {
            val isOn = current[block] ?: continue
            when {
                isOn && first && block !in ours -> onBefore += block
                !isOn -> {
                    actions += Action.TurnOn(block)
                    if (block !in onBefore) ours += block
                }
            }
        }
        // A block that vanished from Curbox is forgotten.
        ours = ours.filter { it in current }.toSet()
        onBefore = onBefore.filter { it in current }.toSet()
        return Step(actions, Memory(ours, onBefore))
    }

    private fun end(current: Map<CurboxBlockRef, Boolean>, memory: Memory): Step {
        val actions = memory.ours.filter { current[it] == true }.map { Action.TurnOff(it) }
        return Step(actions, Memory())
    }
}
