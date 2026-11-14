package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.TaskStepLetters
import java.util.regex.Pattern

/**
 * The search over the task list (the PC's TaskSearch, rule for rule): a number ("142") finds the task by its number (a "142b" its
 * step's task); anything else is words: "@label" (the label's name starts with it, no case; an underscore stands for a space, so
 * "@deep_w" finds "Deep work": the Add box's word rule), "#tag" (a tag starts with it) and plain words (each found, in any order, in
 * the title, a step, the note or a tag). Every word and token must match (they AND); an "@label" no label starts with finds nothing;
 * a lone "@" or "#" is ignored. A query that looks like a number ("142", "142b"; ASCII digits only) is looked up by number first; when
 * no task or step has that number ("10am", "3pm", "1st", "2fa", "4k") it is a word. Label and tag words are letters, marks, digits,
 * connector punctuation and hyphens in any script. Case folding is one simple lower-case mapping per code point ([fold]), the same
 * as the PC's.
 */
object TaskSearch {
    /** The hint in the search boxes. */
    const val HINT = "Search \u00B7 @label #tag"

    private val idPattern = Pattern.compile("^[0-9]+[a-z]{0,4}$")
    private val labelToken = Pattern.compile("^@([\\p{L}\\p{M}\\p{Nd}\\p{Pc}-]+)$")
    private val tagToken = Pattern.compile("^#([\\p{L}\\p{M}\\p{Nd}\\p{Pc}-]+)$")
    private val spaces = Pattern.compile("\\s+", Pattern.UNICODE_CHARACTER_CLASS)

    private class Words(val labels: List<String>, val tags: List<String>, val text: List<String>) {
        val isEmpty: Boolean get() = labels.isEmpty() && tags.isEmpty() && text.isEmpty()
    }

    /** The query looks like a task ID ("142", "142b", case and outer spaces ignored): it is tried as a number first. */
    fun isId(query: String): Boolean = idPattern.matcher(fold(query.trim())).matches()

    /**
     * The case folding of every comparison here, the same as the PC's: each code point through its simple lower-case mapping (a final
     * sigma stays sigma, U+0130 stays as it is), never the whole-string rules of one platform.
     */
    fun fold(text: String): String {
        val folded = StringBuilder(text.length)
        text.codePoints().forEach { codePoint -> folded.appendCodePoint(if (codePoint == 0x130) codePoint else Character.toLowerCase(codePoint)) }
        return folded.toString()
    }

    /** Whether [query] narrows anything: false for blank text and for lone "@" and "#" words. */
    fun hasCriteria(query: String): Boolean = isId(query) || !read(query).isEmpty

    /**
     * The ids of the tasks [query] finds, null when it narrows nothing (show all). A task counts when its text holds every word
     * (title, steps, note, tags) and it passes the "@label" and "#tag" words; an ID finds the task by its number.
     */
    fun matchingIds(
        tasks: List<TaskItem>, query: String, labels: List<TaskLabel>, labelShown: ((TaskLabel) -> Boolean)? = null, tagShown: ((String) -> Boolean)? = null,
    ): Set<String>? {
        if (!hasCriteria(query)) return null
        val live = tasks.filter { it.isLive }
        if (isId(query)) {
            val (task, step) = byNumber(live, fold(query.trim())) ?: (null to null)
            // A step's letter past the task's last step is no number: the query is a word then.
            if (task != null && (step == null || step < task.steps.orEmpty().size)) return setOf(task.id.lowercase())
        }

        val words = read(query)
        val labelIds = labelIds(words, labels, labelShown)
        return live.filter { passesTokens(it, words, labelIds, tagShown) && (words.text.isEmpty() || combinedText(it, words.text, tagShown)) }
            .map { it.id.lowercase() }.toSet()
    }

    // Splits lower-case text on spaces; "@x" and "#x" (word characters and hyphens) are tokens, a lone "@" or "#" is dropped.
    private fun read(query: String): Words {
        val labels = ArrayList<String>()
        val tags = ArrayList<String>()
        val text = ArrayList<String>()
        for (token in spaces.split(fold(query.trim())).filter { it.isNotEmpty() }) {
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
    private fun spelled(name: String): String = spaces.matcher(fold(name.trim())).replaceAll("_")

    // Per "@" word, the ids of the live labels whose spelled name starts with it (an empty set: the word finds nothing).
    private fun labelIds(words: Words, labels: List<TaskLabel>, labelShown: ((TaskLabel) -> Boolean)?): List<Set<String>> =
        words.labels.map { word -> labels.filter { it.isLive && (labelShown == null || labelShown(it)) && spelled(it.name).startsWith(word) }.map { it.id.lowercase() }.toSet() }

    private fun passesTokens(task: TaskItem, words: Words, labelIds: List<Set<String>>, tagShown: ((String) -> Boolean)?): Boolean {
        val label = task.labelId?.lowercase()
        return labelIds.all { ids -> label != null && label in ids } &&
            words.tags.all { word -> shown(task, tagShown).any { fold(it).startsWith(word) } }
    }

    // The task's tags this device shows (ADR 89); all of them without a test.
    private fun shown(task: TaskItem, tagShown: ((String) -> Boolean)?): List<String> = task.tags.orEmpty().filter { tagShown == null || tagShown(it) }

    // Every word is somewhere in the title, a step, the note or a tag ("#home").
    private fun combinedText(task: TaskItem, words: List<String>, tagShown: ((String) -> Boolean)?): Boolean {
        val all = (listOf(task.title, task.note.orEmpty()) + task.steps.orEmpty().map { it.title } + shown(task, tagShown).map { "#$it" })
            .joinToString("\n").let { fold(it) }
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
