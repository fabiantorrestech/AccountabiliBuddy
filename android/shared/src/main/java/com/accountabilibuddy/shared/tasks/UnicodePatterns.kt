package com.accountabilibuddy.shared.tasks

import java.util.regex.Pattern

/**
 * Regexes whose `\w` and `\s` take any language's letters and spaces, as .NET's do on the PC. The desktop JVM (the parity checks)
 * needs [Pattern.UNICODE_CHARACTER_CLASS] for that; Android's regex engine is Unicode-aware already and throws on the flag
 * ("UNICODE_CHARACTER_CLASS flag not supported"), which crashed the Add sheet on the phone (AB-Bugs #28). So the flag is added
 * only where it is supported.
 */
internal object UnicodePatterns {
    private val flagSupported: Boolean = runCatching { Pattern.compile("a", Pattern.UNICODE_CHARACTER_CLASS) }.isSuccess

    fun compile(regex: String, flags: Int = 0): Pattern =
        Pattern.compile(regex, if (flagSupported) flags or Pattern.UNICODE_CHARACTER_CLASS else flags)
}
