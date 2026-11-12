package com.accountabilibuddy.shared.goals

/**
 * ADR 79 forward compatibility on every change to the goal lists (the PC's `GoalUnknowns`, rule for rule): what this app doesn't know
 * is never removed by it. A screen or a rule that builds an item again (a new target, new extra tiles, a reminder from its editor row)
 * leaves out the unknown fields its stored copy had; [keep] puts them back from the copy before the change, and a goal's newer source
 * rules, a review's newer decisions and a list's newer items stay too. Matched by id (an item, a source rule), by goal (a review
 * decision), by first day (a pause), by kind and text (an app entry) and by place (a target, the second target, a waiting change, the
 * extra tiles). An object that has unknown fields (or newer items) of its own keeps those as given.
 */
object GoalUnknowns {
    /** [after] with what [before] held that this version doesn't know put back (the store runs it after every change). */
    fun keep(before: GoalLists, after: GoalLists): GoalLists = after.copy(
        areas = each(before.areas, after.areas, { it.id }, ::keepArea),
        goals = each(before.goals, after.goals, { it.id }, ::keepGoal),
        entries = each(before.entries, after.entries, { it.id }, ::keepEntry),
        reminders = each(before.reminders, after.reminders, { it.id }, ::keepReminder),
        reviews = each(before.reviews, after.reviews, { it.id }, ::keepReview),
        newer = GoalNewerLists(
            keepNewer(before.newer.areas, after.newer.areas),
            keepNewer(before.newer.goals, after.newer.goals),
            keepNewer(before.newer.entries, after.newer.entries),
            keepNewer(before.newer.reminders, after.newer.reminders),
            keepNewer(before.newer.reviews, after.newer.reviews),
        ),
    )

    /** A list's newer items: the ones after the change, or, when it has none, the ones before (nothing here removes them). */
    fun keepNewer(before: List<GoalNewerItem>, after: List<GoalNewerItem>): List<GoalNewerItem> = if (after.isEmpty()) before else after

    fun keepArea(old: GoalArea, now: GoalArea): GoalArea = if (now.extra.isEmpty() && old.extra.isNotEmpty()) now.copy(extra = old.extra) else now

    fun keepEntry(old: GoalEntry, now: GoalEntry): GoalEntry = if (now.extra.isEmpty() && old.extra.isNotEmpty()) now.copy(extra = old.extra) else now

    fun keepReminder(old: GoalReminder, now: GoalReminder): GoalReminder =
        if (now.extra.isEmpty() && old.extra.isNotEmpty()) now.copy(extra = old.extra) else now

    fun keepGoal(old: Goal, now: Goal): Goal {
        var kept = now
        if (now.extra.isEmpty() && old.extra.isNotEmpty()) kept = kept.copy(extra = old.extra)
        keepTarget(old.target, now.target).let { if (it !== now.target) kept = kept.copy(target = it) }
        val oldSecond = old.secondTarget
        val nowSecond = now.secondTarget
        if (oldSecond != null && nowSecond != null) {
            keepTarget(oldSecond, nowSecond).let { if (it !== nowSecond) kept = kept.copy(secondTarget = it) }
        }
        val oldTiles = old.extraTiles
        val nowTiles = now.extraTiles
        if (oldTiles != null && nowTiles != null && nowTiles.extra.isEmpty() && oldTiles.extra.isNotEmpty()) {
            kept = kept.copy(extraTiles = nowTiles.copy(extra = oldTiles.extra))
        }
        val sources = each(old.sources, now.sources, { it.id }, ::keepSourceRule)
        if (sources !== now.sources) kept = kept.copy(sources = sources)
        if (now.newerSources.isEmpty() && old.newerSources.isNotEmpty()) kept = kept.copy(newerSources = old.newerSources)
        val oldPauses = old.pauses
        val nowPauses = now.pauses
        if (oldPauses != null && nowPauses != null) {
            val pauses = each(oldPauses, nowPauses, { it.from }) { was, stretch ->
                if (stretch.extra.isEmpty() && was.extra.isNotEmpty()) stretch.copy(extra = was.extra) else stretch
            }
            if (pauses !== nowPauses) kept = kept.copy(pauses = pauses)
        }
        return kept
    }

    fun keepReview(old: GoalReviewRecord, now: GoalReviewRecord): GoalReviewRecord {
        var kept = if (now.extra.isEmpty() && old.extra.isNotEmpty()) now.copy(extra = old.extra) else now
        val decisions = each(old.decisions, now.decisions, { it.goalId }) { was, decision ->
            if (decision.extra.isEmpty() && was.extra.isNotEmpty()) decision.copy(extra = was.extra) else decision
        }
        if (decisions !== now.decisions) kept = kept.copy(decisions = decisions)
        if (now.newerDecisions.isEmpty() && old.newerDecisions.isNotEmpty()) kept = kept.copy(newerDecisions = old.newerDecisions)
        return kept
    }

    fun keepTarget(old: GoalTarget, now: GoalTarget): GoalTarget {
        var kept = if (now.extra.isEmpty() && old.extra.isNotEmpty()) now.copy(extra = old.extra) else now
        val oldPending = old.pendingChange
        val nowPending = now.pendingChange
        if (oldPending != null && nowPending != null && nowPending.extra.isEmpty() && oldPending.extra.isNotEmpty()) {
            kept = kept.copy(pendingChange = nowPending.copy(extra = oldPending.extra))
        }
        return kept
    }

    fun keepSourceRule(old: GoalSourceRule, now: GoalSourceRule): GoalSourceRule {
        var kept = if (now.extra.isEmpty() && old.extra.isNotEmpty()) now.copy(extra = old.extra) else now
        val oldApps = old.apps
        val nowApps = now.apps
        if (oldApps != null && nowApps != null) {
            val apps = each(oldApps, nowApps, { it.kind to it.text }) { was, app ->
                if (app.extra.isEmpty() && was.extra.isNotEmpty()) app.copy(extra = was.extra) else app
            }
            if (apps !== nowApps) kept = kept.copy(apps = apps)
        }
        return kept
    }

    // Each item of after with its old twin (the first of that key before) passed through keep; after itself when nothing changed.
    private fun <T, K> each(before: List<T>, after: List<T>, key: (T) -> K, keep: (T, T) -> T): List<T> {
        val olds = HashMap<K, T>()
        before.forEach { olds.putIfAbsent(key(it), it) }
        var changed = false
        val kept = after.map { item ->
            val old = olds[key(item)]
            val next = if (old == null) item else keep(old, item)
            if (next !== item) changed = true
            next
        }
        return if (changed) kept else after
    }
}
