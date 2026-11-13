package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.TaskStepLetters
import java.util.regex.Pattern

/**
 * The search over the task list (the PC's TaskSearch, rule for rule): a number ("142") finds the task by its number (a "142b" its
 * step's task); anything else is words: "@label" (the label's name starts with it, no case; an underscore stands for a space, so
 * "@deep_w" finds "Deep work": the Add box's word rule), "#tag" (a tag starts with it) and plain words (each found, in any order, in
 * the title, a step, the note or a tag). Every word and token must match (they AND); an "@label" no label starts with finds nothing;
 * a lone "@" or "#" is ignored.
 */
object TaskSearch {
    /** The hint in the search boxes. */
    const val HINT = "Search \u00B7 @label #tag"

    private val idPattern = Pattern.compile("^\\d+[a-z]{0,4}$")
    private val labelToken = Pattern.compile("^@([\\w-]+)$", Pattern.UNICODE_CHARACTER_CLASS)
    private val tagToken = Pattern.compile("^#([\\w-]+)$", Pattern.UNICODE_CHARACTER_CLASS)
    private val spaces = Pattern.compile("\\s+", Pattern.UNICODE_CHARACTER_CLASS)

    private class Words(val labels: List<String>, val tags: List<String>, val text: List<String>) {
        val isEmpty: Boolean get() = labels.isEmpty() && tags.isEmpty() && text.isEmpty()
    }

    /** The query is a task ID ("142", "142b", case and outer spaces ignored). */
    fun isId(query: String): Boolean = idPattern.matcher(query.trim().lowercase()).matches()

    /** Whether [query] narrows anything: false for blank text and for lone "@" and "#" words. */
    fun hasCriteria(query: String): Boolean = isId(query) || !read(query).isEmpty

    /**
     * The ids of the tasks [query] finds, null when it narrows nothing (show all). A task counts when its text holds every word
     * (title, steps, note, tags) and it passes the "@label" and "#tag" words; an ID finds the task by its number.
     */
    fun matchingIds(tasks: List<TaskItem>, query: String, labels: List<TaskLabel>): Set<String>? {
        if (!hasCriteria(query)) return null
        val live = tasks.filter { it.isLive }
        if (isId(query)) {
            val (task, step) = byNumber(live, query.trim().lowercase()) ?: return emptySet()
            // A step's letter past the task's last step finds nothing.
            return if (step == null || step < task.steps.orEmpty().size) setOf(task.id.lowercase()) else emptySet()
        }

        val words = read(query)
        val labelIds = labelIds(words, labels)
        return live.filter { passesTokens(it, words, labelIds) && (words.text.isEmpty() || combinedText(it, words.text)) }
            .map { it.id.lowercase() }.toSet()
    }

    // Splits lower-case text on spaces; "@x" and "#x" (word characters and hyphens) are tokens, a lone "@" or "#" is dropped.
    private fun read(query: String): Words {
        val labels = ArrayList<String>()
        val tags = ArrayList<String>()
        val text = ArrayList<String>()
        for (token in spaces.split(query.trim().lowercase()).filter { it.isNotEmpty() }) {
            if (token == "@" || token == "#") continue
            val label = labelToken.matcher(token)
            val tag = tagToken.matcher(token)
            when {
                label.matches() -> labels += label.group(1)!!
                tag.matches() -> tags += tag.group(1)!!
                else -> text += token
            }
        }

        return Words(labels, tags, text)
    }

    // A label's name as an "@" word spells it: lower case, spaces as underscores.
    private fun spelled(name: String): String = spaces.matcher(name.trim().lowercase()).replaceAll("_")

    // Per "@" word, the ids of the live labels whose spelled name starts with it (an empty set: the word finds nothing).
    private fun labelIds(words: Words, labels: List<TaskLabel>): List<Set<String>> =
        words.labels.map { word -> labels.filter { it.isLive && spelled(it.name).startsWith(word) }.map { it.id.lowercase() }.toSet() }

    private fun passesTokens(task: TaskItem, words: Words, labelIds: List<Set<String>>): Boolean {
        val label = task.labelId?.lowercase()
        return labelIds.all { ids -> label != null && label in ids } &&
            words.tags.all { word -> task.tags.orEmpty().any { it.lowercase().startsWith(word) } }
    }

    // Every word is somewhere in the title, a step, the note or a tag ("#home").
    private fun combinedText(task: TaskItem, words: List<String>): Boolean {
        val all = (listOf(task.title, task.note.orEmpty()) + task.steps.orEmpty().map { it.title } + task.tags.orEmpty().map { "#$it" })
            .joinToString("\n").lowercase()
        return words.all { all.contains(it) }
    }

    // The live task a reference like "142" or "142b" points to (and the step's place, null for the task): a final number first, then a
    // current temporary one, then a former one.
    private fun byNumber(live: List<TaskItem>, reference: String): Pair<TaskItem, Int?>? {
        var digits = 0
        while (digits < reference.length && reference[digits] in '0'..'9') digits++
        val number = reference.substring(0, digits).toIntOrNull() ?: return null
        val step = if (digits < reference.length) indexOfLetters(reference.substring(digits)) ?: return null else null
        val task = live.firstOrNull { it.number != null && !it.number.temporary && it.number.value == number }
            ?: live.firstOrNull { it.number != null && it.number.temporary && it.number.value == number }
            ?: live.firstOrNull { it.formerNumbers?.contains(number) == true }
        return task?.let { it to step }
    }

    // A step's letters as a 0-based place ("a" = 0, "aa" = 26); null unless 1 to 4 lower-case letters (TaskStepLetters.of's inverse).
    private fun indexOfLetters(letters: String): Int? {
        if (letters.isEmpty() || letters.length > 4 || !letters.all { it in 'a'..'z' }) return null
        val index = letters.fold(0) { sum, letter -> sum * 26 + (letter - 'a' + 1) } - 1
        return index.takeIf { TaskStepLetters.of(it) == letters }
    }
}
