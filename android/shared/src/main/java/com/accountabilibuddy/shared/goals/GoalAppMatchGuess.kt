package com.accountabilibuddy.shared.goals

/** Guesses the kind of a typed entry; the user can change it afterwards. Twin of Core's `GoalAppMatchGuess`. */
object GoalAppMatchGuess {
    private val knownTlds = setOf("com", "net", "org", "io", "app", "dev", "tv", "gg", "co", "me", "ai", "edu", "gov")

    /**
     * Space -> Name; ends ".exe" -> Process; exactly two dotted labels with a plausible top-level domain -> Website; other dotted
     * text -> Package; anything else -> Name. Trimmed; empty -> null.
     */
    fun guess(text: String?): GoalAppMatch? {
        val trimmed = text?.trim().orEmpty()
        if (trimmed.isEmpty()) return null
        if (trimmed.any { it.isWhitespace() }) return GoalAppMatch(GoalAppMatchKind.Name, trimmed)
        if (trimmed.endsWith(".exe", ignoreCase = true)) return GoalAppMatch(GoalAppMatchKind.Process, trimmed)
        if (!trimmed.contains('.')) return GoalAppMatch(GoalAppMatchKind.Name, trimmed)

        val labels = trimmed.split('.')
        val website = labels.size == 2 && labels[0].isNotEmpty() && isPlausibleTld(labels[1])
        return GoalAppMatch(if (website) GoalAppMatchKind.Website else GoalAppMatchKind.Package, trimmed)
    }

    private fun isPlausibleTld(label: String): Boolean {
        if (label.length !in 2..6 || !label.all { it in 'a'..'z' || it in 'A'..'Z' }) return false
        return label.length == 2 || label.lowercase() in knownTlds
    }
}
