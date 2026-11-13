package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.DotNetTime
import com.accountabilibuddy.shared.sync.TaskLabels
import java.time.LocalDate
import java.time.LocalTime
import java.time.format.DateTimeParseException

/**
 * What a view shows and in what order (ADR 76; the PC's TaskViewRules, rule for rule): [apply] filters ([matches]), groups (bins,
 * label, tag, none) and sorts (manual, due, newest, oldest, untouched, shuffle, longest, shortest), with highlighted tasks first when
 * the view pins them. Ticks inside one filter widen it (any can match), different filters narrow it (each must match); a value from a
 * newer app matches nothing. Sort and group values this app doesn't know fall back to manual order and bins.
 *
 * The search here is the phone's own (every word in the title, a step, the note or a tag); the PC's ranked search isn't mirrored.
 */
object TaskViewRules {
    const val SORT_DUE_ASCENDING = "dueAsc"
    const val SORT_DUE_DESCENDING = "dueDesc"
    const val SORT_NEWEST = "newest"
    const val SORT_OLDEST = "oldest"
    const val SORT_UNTOUCHED = "untouched"
    const val SORT_SHUFFLE = "shuffle"
    const val SORT_LONGEST = "longest"
    const val SORT_SHORTEST = "shortest"
    const val LABEL_NONE = "label:none"
    const val TAG_NONE = "tag:none"

    private const val WEEK_DAYS = 7L
    private val GUID_BYTE_ORDER = intArrayOf(3, 2, 1, 0, 5, 4, 7, 6, 8, 9, 10, 11, 12, 13, 14, 15)

    fun apply(tasks: List<TaskItem>, config: TaskViewConfig, context: TaskViewContext): TaskViewResult {
        val inScope = tasks.filter { it.isLive && (context.scope == null || it.bin in context.scope) }
        val words = context.search?.trim()?.lowercase()?.split(' ', '\t')?.filter { it.isNotEmpty() }.orEmpty()
        val shown = inScope.filter { task -> (words.isEmpty() || found(task, words)) && matches(task, config, context) }
        val order = order(config)
        val groups = when (config.group) {
            TaskViewConfig.GROUP_LABEL -> byLabel(shown, context)
            TaskViewConfig.GROUP_TAG -> byTag(shown)
            TaskViewConfig.GROUP_NONE -> listOf(TaskViewGroup("all", "All", null, null, null, shown))
            else -> byBin(shown, context)
        }
        return TaskViewResult(groups.map { it.copy(tasks = it.tasks.sortedWith(order)) })
    }

    /** Whether a task passes the view's filters (not the search words). */
    fun matches(task: TaskItem, config: TaskViewConfig, context: TaskViewContext): Boolean {
        val bins = config.bins
        if (!bins.isNullOrEmpty() && task.bin !in bins) return false
        val only = config.only
        val local = context.now.atZoneSameInstant(context.zone)
        val today = local.toLocalDate()
        val time = local.toLocalTime()
        return (only.due.isNullOrEmpty() || only.due.any { dueMatches(task, it, today, time) }) &&
            (only.time.isNullOrEmpty() || only.time.any { timeMatches(task, it) }) &&
            (only.old.isNullOrEmpty() || only.old.any { oldMatches(task, it, context) }) &&
            (!only.highlightedOnly || task.highlight) &&
            labelMatches(task, config) &&
            tagsMatch(task, config)
    }

    /** Open and past its due day (or, due today with a time, past that time). */
    fun isOverdue(task: TaskItem, today: LocalDate, time: LocalTime): Boolean {
        val due = task.dueOn ?: return false
        return !task.isDone && (due < today || (due == today && task.dueAt != null && task.dueAt < time))
    }

    private fun found(task: TaskItem, words: List<String>): Boolean {
        val text = (listOf(task.title, task.note.orEmpty()) + task.steps.orEmpty().map { it.title } + task.tags.orEmpty().map { "#$it" })
            .joinToString("\n").lowercase()
        return words.all { text.contains(it) }
    }

    private fun dueMatches(task: TaskItem, pick: String, today: LocalDate, time: LocalTime): Boolean {
        if (pick == "none") return task.dueOn == null
        val due = task.dueOn ?: return false
        if (pick.startsWith("range:")) {
            val ends = pick.substring("range:".length).split("..")
            if (ends.size != 2) return false
            return try {
                due >= LocalDate.parse(ends[0]) && due <= LocalDate.parse(ends[1])
            } catch (e: DateTimeParseException) {
                false
            }
        }

        return when (pick) {
            "overdue" -> isOverdue(task, today, time)
            "today" -> due == today
            "tomorrow" -> due == today.plusDays(1)
            "thisWeek" -> due >= today && due <= today.plusDays(WEEK_DAYS - 1)
            else -> false
        }
    }

    private fun timeMatches(task: TaskItem, pick: String): Boolean {
        val minutes = task.estimateMinutes
        return when (pick) {
            "le10" -> minutes != null && minutes <= 10
            "le25" -> minutes != null && minutes <= 25
            "le50" -> minutes != null && minutes <= 50
            "ge50" -> minutes != null && minutes >= 50
            "none" -> minutes == null
            else -> false
        }
    }

    private fun oldMatches(task: TaskItem, pick: String, context: TaskViewContext): Boolean {
        if (pick.length < 2 || pick.last() != 'w') return false
        val digits = pick.dropLast(1)
        if (!digits.all { it in '0'..'9' }) return false
        val weeks = digits.toIntOrNull() ?: return false
        return TaskUntouched.isOlderThan(task, weeks, context.now)
    }

    private fun labelMatches(task: TaskItem, config: TaskViewConfig): Boolean {
        if (config.labels.isNullOrEmpty() && !config.noLabel) return true
        val label = task.labelId ?: return config.noLabel
        return config.labels?.any { it.equals(label, ignoreCase = true) } == true
    }

    private fun tagsMatch(task: TaskItem, config: TaskViewConfig): Boolean {
        val tags = config.tags
        if (tags.isNullOrEmpty()) return true
        return if (config.match == TaskViewConfig.MATCH_ALL) tags.all { TaskLabels.has(task.tags, it) } else tags.any { TaskLabels.has(task.tags, it) }
    }

    private fun byBin(shown: List<TaskItem>, context: TaskViewContext): List<TaskViewGroup> {
        val bins = if (context.showEmptyBins) context.scope ?: TaskBin.entries else shown.map { it.bin }.distinct().sorted()
        return bins.map { bin -> TaskViewGroup("bin:" + bin.name.lowercase(), TaskBins.name(bin), bin, null, null, shown.filter { it.bin == bin }) }
    }

    private fun byLabel(shown: List<TaskItem>, context: TaskViewContext): List<TaskViewGroup> {
        val groups = ArrayList<TaskViewGroup>()
        val live = context.labels.filter { it.isLive }
        for (label in live.sortedWith(compareBy<TaskLabel> { it.position }.thenBy(String.CASE_INSENSITIVE_ORDER) { it.name })) {
            val of = shown.filter { it.labelId.equals(label.id, ignoreCase = true) }
            if (of.isNotEmpty()) groups += TaskViewGroup("label:" + label.id.lowercase(), label.name, null, label.id, null, of)
        }

        val known = live.map { it.id.lowercase() }.toSet()
        val without = shown.filter { it.labelId == null || it.labelId.lowercase() !in known }
        if (without.isNotEmpty()) groups += TaskViewGroup(LABEL_NONE, "No label", null, null, null, without)
        return groups
    }

    private fun byTag(shown: List<TaskItem>): List<TaskViewGroup> {
        val spellings = ArrayList<String>()
        for (tag in shown.flatMap { it.tags.orEmpty() }) {
            if (spellings.none { TaskLabels.same(it, tag) }) spellings += tag
        }

        val groups = spellings.sortedWith(String.CASE_INSENSITIVE_ORDER)
            .map { tag -> TaskViewGroup("tag:" + tag.lowercase(), "#$tag", null, null, tag, shown.filter { TaskLabels.has(it.tags, tag) }) }
            .toMutableList()
        val without = shown.filter { it.tags.isNullOrEmpty() }
        if (without.isNotEmpty()) groups += TaskViewGroup(TAG_NONE, "No tag", null, null, null, without)
        return groups
    }

    // Highlighted first (when pinned), then the sort, then the manual order (bin, place, age, id) so equal tasks never swap places.
    private fun order(config: TaskViewConfig): Comparator<TaskItem> {
        val sort = sort(config)
        return Comparator { first, second ->
            var result = if (config.pinHighlights) second.highlight.compareTo(first.highlight) else 0
            if (result == 0) result = sort.compare(first, second)
            if (result == 0) result = manual(first, second)
            result
        }
    }

    private fun manual(first: TaskItem, second: TaskItem): Int {
        var result = first.bin.compareTo(second.bin)
        if (result == 0) result = first.position.compareTo(second.position)
        if (result == 0) result = DotNetTime.compare(first.createdAt, second.createdAt)
        // The PC compares the ids as Guids: the same order as their lower-case text.
        return if (result != 0) result else first.id.lowercase().compareTo(second.id.lowercase())
    }

    private fun sort(config: TaskViewConfig): Comparator<TaskItem> {
        val seed = config.seed ?: 0
        return when (config.sort) {
            SORT_DUE_ASCENDING -> Comparator { a, b -> compareLast(dueKey(a), dueKey(b), descending = false) }
            SORT_DUE_DESCENDING -> Comparator { a, b -> compareLast(dueKey(a), dueKey(b), descending = true) }
            SORT_NEWEST -> Comparator { a, b -> DotNetTime.compare(b.createdAt, a.createdAt) }
            SORT_OLDEST -> Comparator { a, b -> DotNetTime.compare(a.createdAt, b.createdAt) }
            SORT_UNTOUCHED -> Comparator { a, b -> DotNetTime.compare(a.touchedAt, b.touchedAt) }
            SORT_SHUFFLE -> Comparator { a, b -> shuffleKey(a.id, seed).compareTo(shuffleKey(b.id, seed)) }
            SORT_LONGEST -> Comparator { a, b -> compareLast(a.estimateMinutes?.toLong(), b.estimateMinutes?.toLong(), descending = true) }
            SORT_SHORTEST -> Comparator { a, b -> compareLast(a.estimateMinutes?.toLong(), b.estimateMinutes?.toLong(), descending = false) }
            else -> Comparator { _, _ -> 0 }
        }
    }

    // The due day and time as one number that orders like the PC's ticks.
    private fun dueKey(task: TaskItem): Long? = task.dueOn?.let { it.toEpochDay() * 86_400_000_000_000L + (task.dueAt?.toNanoOfDay() ?: 0L) }

    // Tasks with no value (no due date, no time limit) go last in either direction.
    private fun compareLast(first: Long?, second: Long?, descending: Boolean): Int {
        if (first == null || second == null) return if (first == null) (if (second == null) 0 else 1) else -1
        return if (descending) second.compareTo(first) else first.compareTo(second)
    }

    /**
     * A fixed hash (FNV-1a over the id's bytes in the PC's Guid byte order, then the seed's four bytes, low byte first), so a stored
     * seed gives the same order on every device.
     */
    internal fun shuffleKey(id: String, seed: Int): Long {
        var hash = 2166136261L
        fun mix(value: Int) {
            hash = ((hash xor (value.toLong() and 0xFF)) * 16777619L) and 0xFFFFFFFFL
        }

        val hex = id.filter { it != '-' }
        if (hex.length == 32 && hex.all { it in '0'..'9' || it in 'a'..'f' || it in 'A'..'F' }) {
            val bytes = IntArray(16) { hex.substring(it * 2, it * 2 + 2).toInt(16) }
            // .NET writes a Guid's first three groups low byte first.
            for (index in GUID_BYTE_ORDER) mix(bytes[index])
        } else {
            id.toByteArray().forEach { mix(it.toInt()) }
        }

        for (shift in 0..3) mix(seed ushr (8 * shift))
        return hash
    }
}
