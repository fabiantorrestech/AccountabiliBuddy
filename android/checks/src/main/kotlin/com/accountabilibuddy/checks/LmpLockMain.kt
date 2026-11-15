package com.accountabilibuddy.checks

import kotlin.system.exitProcess

/** Runs the LockMyPhone-Extended lock checks; exit code 0 only when everything passed. */
fun main() {
    val report = CheckReport()
    lmpLockSelfCheck(report)
    report.printTotal()
    exitProcess(if (report.allPassed) 0 else 1)
}
