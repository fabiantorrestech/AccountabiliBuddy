package com.accountabilibuddy.checks

import java.io.File
import kotlin.system.exitProcess

/** Every Kotlin parity check; add one per fixture file the C# checks write. */
private val checks: List<FixtureCheck> = listOf(
    PlannedTieTextCheck(),
    StepLettersCheck(),
)

/**
 * Runs each check over its fixture file. A check whose file is missing fails (the C# checks didn't write it), so a broken
 * pipeline can't pass quietly. Exit code 0 only when everything passed.
 * Arguments: `--fixtures <dir>` (default %TEMP%\claude\round2-fixtures), `--area <name>` to run one area only.
 */
fun main(args: Array<String>) {
    val root = File(option(args, "--fixtures") ?: File(System.getProperty("java.io.tmpdir"), "claude/round2-fixtures").path)
    val onlyArea = option(args, "--area")
    println("fixtures: ${root.absolutePath}")
    val report = CheckReport()
    for (check in checks.filter { onlyArea == null || it.area == onlyArea }) {
        val fixture = FixtureFile.readArea(root, check.area).firstOrNull { it.name == check.name }
        if (fixture == null) {
            report.check("${check.area}/${check.name}: fixture file present", false, "run the C# checks first (they write ${check.area}/${check.name}.json)")
            continue
        }

        report.check("${check.area}/${check.name}: fixture has cases (from ${fixture.source})", fixture.cases.isNotEmpty())
        check.run(fixture, report)
    }

    report.printTotal()
    exitProcess(if (report.allPassed) 0 else 1)
}

private fun option(args: Array<String>, name: String): String? = args.indexOf(name).takeIf { it >= 0 && it + 1 < args.size }?.let { args[it + 1] }
