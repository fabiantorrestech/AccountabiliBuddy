package com.accountabilibuddy.checks

/** Collects PASS/FAIL lines in the same form as the C# check programs and prints the total. */
class CheckReport {
    private var passed = 0
    private var failed = 0

    fun check(name: String, ok: Boolean, detail: String = "") {
        println((if (ok) "PASS  " else "FAIL  ") + name + if (ok || detail.isEmpty()) "" else "  -- $detail")
        if (ok) passed++ else failed++
    }

    val allPassed: Boolean get() = failed == 0

    fun printTotal() = println("$passed/${passed + failed} passed")
}
