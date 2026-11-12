package com.accountabilibuddy.checks

/** Runs one fixture case's comparison: a returned difference, or a throw, is a FAIL line with the reason. */
object GoalCaseRunner {
    fun run(report: CheckReport, name: String, compare: () -> String?) {
        val result = runCatching(compare)
        val detail = result.getOrElse { "threw ${it::class.simpleName}: ${it.message}" }
        report.check(name, result.isSuccess && detail == null, detail ?: "")
    }

    /** Null when both texts are the same bytes, else where they first differ. */
    fun sameText(expected: String, actual: String): String? {
        if (expected == actual) return null
        val at = expected.zip(actual).indexOfFirst { (a, b) -> a != b }.let { if (it < 0) minOf(expected.length, actual.length) else it }
        return "texts differ at $at: C# …${expected.substring(maxOf(0, at - 30), minOf(expected.length, at + 30))}… vs kotlin …" +
            "${actual.substring(maxOf(0, at - 30), minOf(actual.length, at + 30))}…"
    }
}
