package com.accountabilibuddy.shared.sync

/**
 * A task's labels (ADR 72): plain names, no colour, several per task, cutting across categories. Compared case-insensitively
 * ("Home" = "home"), shown as the first spelling seen. In a typed title, `##word` adds the label "word" and leaves the title; a
 * single `#` stays text ("#1 priority"). The PC's twin is `TaskLabels` in Core; both give the same result.
 */
object TaskLabels {
    const val MAX_PER_TASK = 10
    const val MAX_LENGTH = 40
    const val MARKER = "##"

    /** Trimmed, empty ones dropped, cut to [MAX_LENGTH], case-insensitive duplicates dropped, at most [MAX_PER_TASK]; none gives null. */
    fun clean(labels: List<String>?): List<String>? {
        if (labels == null) return null
        val kept = mutableListOf<String>()
        for (raw in labels) {
            var label = raw.trim()
            if (label.length > MAX_LENGTH) label = label.substring(0, MAX_LENGTH).trimEnd()
            if (label.isEmpty() || kept.any { same(it, label) }) continue
            kept += label
            if (kept.size == MAX_PER_TASK) break
        }

        return kept.ifEmpty { null }
    }

    fun same(first: String, second: String): Boolean = first.trim().equals(second.trim(), ignoreCase = true)

    fun has(labels: List<String>?, label: String): Boolean = labels?.any { same(it, label) } == true

    fun with(existing: List<String>?, added: List<String>): List<String>? = clean(existing.orEmpty() + added)

    fun without(labels: List<String>?, label: String): List<String>? = clean(labels.orEmpty().filterNot { same(it, label) })

    fun renamed(labels: List<String>?, from: String, to: String): List<String>? =
        if (!has(labels, from)) labels else clean(labels!!.map { if (same(it, from)) to else it })

    /** "Fix printer ##home" gives ("Fix printer", ["home"]); a `##` with nothing after it, or `###`, stays text. */
    fun parse(typed: String): Pair<String, List<String>?> {
        if (!typed.contains(MARKER)) return typed to null
        val labels = mutableListOf<String>()
        val words = mutableListOf<String>()
        for (word in typed.split(' ')) {
            if (word.startsWith(MARKER) && word.length > MARKER.length && word[MARKER.length] != '#') labels += word.substring(MARKER.length) else words += word
        }

        return words.filter { it.isNotEmpty() }.joinToString(" ").trim() to clean(labels)
    }

    fun tieText(labels: List<String>?): String = labels?.joinToString("\u0004") ?: ""

    /** Every label, once each (case-insensitive, first spelling seen), sorted by name. */
    fun allOf(lists: List<List<String>?>): List<String> {
        val all = mutableListOf<String>()
        for (label in lists.filterNotNull().flatten()) if (all.none { same(it, label) }) all += label
        return all.sortedWith(String.CASE_INSENSITIVE_ORDER)
    }

    /** The tie-break parts ADR 72 added to shared items, each only when set (as on the PC). */
    fun tieExtras(labels: List<String>?, localTo: String?): List<String> =
        listOfNotNull(labels?.takeIf { it.isNotEmpty() }?.let { tieText(it) }, localTo?.lowercase())

    /** "#home #work" for a row; empty for none. */
    fun text(labels: List<String>?): String = labels.orEmpty().joinToString(" ") { "#$it" }
}
