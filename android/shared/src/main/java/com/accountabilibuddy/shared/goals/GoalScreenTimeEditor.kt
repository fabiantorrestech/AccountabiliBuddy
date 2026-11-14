package com.accountabilibuddy.shared.goals

import java.util.UUID

/**
 * The edits of a screen-time source's list of apps and sites and its devices (ST3; the PC's editor twin). Pure: the sheet only draws.
 * Entries are private text of the name class: never logged.
 */
object GoalScreenTimeEditor {
    const val MAX_ENTRIES = 50
    const val MAX_TEXT = 120

    /**
     * Whether the other linked devices can be ticked. ADR 79 is Accepted (owner, 2026-11-14), so they can; one flag, so the picker greys them
     * again ("Needs goals sync (ADR 79)") without a UI change if that ever has to be undone.
     */
    const val OTHER_DEVICES_ENABLED = true
    const val NEEDS_GOALS_SYNC = "Needs goals sync (ADR 79)"

    /** The notes the list shows under an entry. */
    const val NOT_SEEN = "Not seen on this device yet"
    const val PCS_ONLY = "Counts on PCs only"

    /** The kinds a chip cycles through, in order. */
    val KINDS = listOf(GoalAppMatchKind.Package, GoalAppMatchKind.Process, GoalAppMatchKind.Name, GoalAppMatchKind.Website)

    fun kindLabel(kind: GoalAppMatchKind): String = when (kind) {
        GoalAppMatchKind.Package -> "App id"
        GoalAppMatchKind.Process -> "Process"
        GoalAppMatchKind.Name -> "Name"
        GoalAppMatchKind.Website -> "Website"
    }

    fun nextKind(kind: GoalAppMatchKind): GoalAppMatchKind = KINDS[(KINDS.indexOf(kind) + 1) % KINDS.size]

    /** A new rule for this device: it counts here unless the picker says more ([createdOn] is stamped now, once). */
    fun newRule(id: UUID, createdOn: UUID): GoalSourceRule =
        GoalSourceRule(id, GoalSourceType.ScreenTime, apps = emptyList(), createdOn = createdOn)

    /** The entry [typed] would make (kind guessed, [GoalAppMatchGuess]); null when blank. */
    fun guess(typed: String): GoalAppMatch? = GoalAppMatchGuess.guess(typed.take(MAX_TEXT))

    /** [rule] with an entry added: [kind] when the user changed the guess, else the guess. Same kind and text (any case) twice, a blank text and a list at its cap change nothing. */
    fun add(rule: GoalSourceRule, typed: String, kind: GoalAppMatchKind? = null): GoalSourceRule {
        val guessed = guess(typed) ?: return rule
        val entry = if (kind == null) guessed else guessed.copy(kind = kind)
        val apps = rule.apps.orEmpty()
        if (apps.size >= MAX_ENTRIES || apps.any { it.kind == entry.kind && it.text.equals(entry.text, ignoreCase = true) }) return rule
        return rule.copy(apps = apps + entry)
    }

    /** [rule] with the entry at [index] set to the next kind (a chip tap). */
    fun cycleKind(rule: GoalSourceRule, index: Int): GoalSourceRule {
        val apps = rule.apps.orEmpty()
        if (index !in apps.indices) return rule
        return rule.copy(apps = apps.mapIndexed { i, entry -> if (i == index) entry.copy(kind = nextKind(entry.kind)) else entry })
    }

    fun remove(rule: GoalSourceRule, index: Int): GoalSourceRule {
        val apps = rule.apps.orEmpty()
        return if (index in apps.indices) rule.copy(apps = apps.filterIndexed { i, _ -> i != index }) else rule
    }

    /** The note under an entry, or null when there is nothing to say. [seen]: the packages and shown names this phone has (lower case). */
    fun note(entry: GoalAppMatch, seen: Set<String>): String? = when (entry.kind) {
        GoalAppMatchKind.Process, GoalAppMatchKind.Website -> PCS_ONLY
        GoalAppMatchKind.Package -> if (entry.text in seen) null else NOT_SEEN
        GoalAppMatchKind.Name -> if (entry.text.lowercase() in seen) null else NOT_SEEN
    }

    /** What the list searches: the pick list's own filter (name or id contains the typed text, any case). */
    fun matchesTyped(typed: String, name: String, id: String): Boolean {
        val needle = typed.trim()
        return needle.isEmpty() || name.contains(needle, ignoreCase = true) || id.contains(needle, ignoreCase = true)
    }

    // ---------------------------------------------------------------- devices

    /** The devices that count now: everyone ([known]) when the rule says so, else its list, else the device that made it, else [self]. */
    fun selected(rule: GoalSourceRule, self: UUID, known: List<UUID>): Set<UUID> = when {
        rule.allDevices -> (known + self).toSet()
        rule.devices != null -> rule.devices.toSet()
        rule.createdOn != null -> setOf(rule.createdOn)
        else -> setOf(self)
    }

    /**
     * [rule] with [device] switched on or off. The list is kept explicit once touched; [GoalSourceRule.createdOn] stays; at least one
     * device must count, so switching the last one off changes nothing.
     */
    fun withDevice(rule: GoalSourceRule, device: UUID, on: Boolean, self: UUID, known: List<UUID>): GoalSourceRule {
        val now = selected(rule, self, known)
        val next = if (on) now + device else now - device
        if (next.isEmpty() || next == now) return rule
        // The listed order is stable: this device first, then the others by id text.
        val ordered = next.sortedWith(compareBy({ it != self }, { it.toString() }))
        return rule.copy(devices = ordered, allDevices = false)
    }

    /** Whether the rule counts anywhere at all: an entry and a device (the device is never empty here). */
    fun problem(rule: GoalSourceRule): String? =
        if (rule.apps.isNullOrEmpty()) "Add at least one app or site for screen time to count." else null
}
