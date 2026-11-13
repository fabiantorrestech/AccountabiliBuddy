package com.accountabilibuddy.shared.tasks

import com.accountabilibuddy.shared.sync.TaskLabels
import java.util.regex.Pattern

/**
 * The Add sheet's reading of a typed title (round 2 R7.3b; board Tasks-Phone-Add; the PC's `TaskTitleHelp.Parse`): "@work" names the
 * label when one has that name (any other "@word" stays text), "#home" is a tag; both leave the title, whose spaces are tidied.
 * And the soft limit's counter (60 characters, shown from 50). Rule-based and local; none of it blocks adding a task.
 */
object TaskTitleHelp {
    const val SOFT_LIMIT = 60
    const val COUNTER_FROM = 50

    // Unicode word characters, as .NET's \w (the flag changes nothing on Android, where they always are).
    private val labelWord = Pattern.compile("(^|\\s)@([\\w-]+)", Pattern.UNICODE_CHARACTER_CLASS)
    private val tagWord = Pattern.compile("(^|\\s)#([\\w-]+)", Pattern.UNICODE_CHARACTER_CLASS)
    private val spaces = Pattern.compile("\\s+", Pattern.UNICODE_CHARACTER_CLASS)

    fun parse(text: String, labels: List<TaskLabel>): TaskTypedTitle {
        var label: String? = null
        var rest = replace(labelWord, text) { lead, word ->
            val named = labels.firstOrNull { it.isLive && it.name.equals(word, ignoreCase = true) }
            if (named == null) {
                null
            } else {
                if (label == null) label = named.id
                lead
            }
        }
        val tags = ArrayList<String>()
        rest = replace(tagWord, rest) { lead, word ->
            tags += word
            lead
        }
        return TaskTypedTitle(spaces.matcher(rest).replaceAll(" ").trim(), label, TaskLabels.clean(tags).orEmpty())
    }

    /**
     * The places of the "@label" and "#tag" words [parse] takes out of [text], for the field to tint them: each from its "@" or "#"
     * to the end of the word.
     */
    fun marks(text: String, labels: List<TaskLabel>): List<IntRange> {
        val found = ArrayList<IntRange>()
        val label = labelWord.matcher(text)
        while (label.find()) {
            val word = label.group(2)!!
            if (labels.any { it.isLive && it.name.equals(word, ignoreCase = true) }) found += label.start(2) - 1 until label.end(2)
        }

        val tag = tagWord.matcher(text)
        while (tag.find()) found += tag.start(2) - 1 until tag.end(2)
        return found.sortedBy { it.first }
    }

    /** [text] without the word [mark] ("@ePT", "#Release": a chip's ×), compared without case; the rest stays as typed. */
    fun without(text: String, mark: String): String {
        val pattern = Pattern.compile("(^|\\s)" + Pattern.quote(mark) + "(\\s+|$)", Pattern.UNICODE_CHARACTER_CLASS or Pattern.CASE_INSENSITIVE or Pattern.UNICODE_CASE)
        // In the middle the space before it stays (one space between its neighbours); at the end nothing is left behind.
        return replace(pattern, text) { lead, after -> if (after.isEmpty()) "" else lead }.trimStart()
    }

    /** The counter under the field: "52 / 60" from 50 characters of title on; null before. */
    fun counter(title: String): String? = if (title.length >= COUNTER_FROM) "${title.length} / $SOFT_LIMIT" else null

    /** Whether the title is past the soft limit (the counter turns amber; adding still works). */
    fun isLong(title: String): Boolean = title.length > SOFT_LIMIT

    // Replaces each match by what [with] gives for its leading space and its word; null leaves the match as it is.
    private fun replace(pattern: Pattern, text: String, with: (String, String) -> String?): String {
        val matcher = pattern.matcher(text)
        val result = StringBuilder()
        var from = 0
        while (matcher.find()) {
            result.append(text, from, matcher.start())
            val word = if (matcher.groupCount() >= 2) matcher.group(2).orEmpty() else ""
            result.append(with(matcher.group(1).orEmpty(), word) ?: matcher.group())
            from = matcher.end()
        }

        result.append(text, from, text.length)
        return result.toString()
    }
}
