package com.accountabilibuddy.checks

import java.io.File
import kotlin.system.exitProcess

/** Every Kotlin parity check; add one per fixture file the C# checks write. */
private val checks: List<FixtureCheck> = listOf(
    PlannedTieTextCheck(),
    StepLettersCheck(),
    // Round 2 R4.7: the goals mirror against the C# Core (GoalsChecks writes goals/*.json).
    GoalSnapshotCheck("attribution"),
    GoalSnapshotCheck("back-fill"),
    GoalSnapshotCheck("days"),
    GoalSnapshotCheck("deep-work"),
    GoalSnapshotCheck("heat"),
    GoalSnapshotCheck("month"),
    GoalSnapshotCheck("pause"),
    GoalSnapshotCheck("review"),
    GoalSnapshotCheck("status"),
    GoalSnapshotCheck("streak"),
    GoalSnapshotCheck("week-start"),
    GoalSnapshotCheck("what-it-takes"),
    GoalAgeCheck(),
    GoalDstCheck(),
    GoalLoweringCheck(),
    GoalLoweringApplyCheck(),
    GoalPeriodsCheck(),
    GoalReminderCheck(),
    GoalRoundingCheck(),
    // R4 milestone review: parity coverage (stored shape byte for byte, numbers, into-ab, sources, label ids, heatmap, strip cells).
    GoalShapeCheck(),
    GoalNumbersCheck(),
    GoalIntoAbCheck(),
    GoalSourcesCheck(),
    GoalLabelIdsCheck(),
    GoalHeatmapGridCheck(),
    GoalHeatmapStepCheck(),
    GoalHeatmapFitCheck(),
    GoalStripCellCheck(),
    // Round 2 R6a: the phone's Goals list against the PC's App (goals-ui/*.json, written by GoalsUiFixtures).
    GoalsUiRowsCheck(),
    GoalsUiWeekCheck(),
    GoalsUiTextsCheck(),
    // Round 2 R6b1: the goal page (tiles, day panel, source lines, What it takes, heatmap) against the PC's detail pane.
    GoalsUiDetailCheck(),
    // Round 2 R6b2: the phone's Deep work weekly target (the looser-change wait) against the PC's GoalLowering fixtures.
    GoalWeeklyParityCheck("lowering"),
    GoalWeeklyParityCheck("lowering-apply"),
)

/**
 * Checks of pure Kotlin with no fixture (their C# twins run on a database, not as a pure function): run every time, after the
 * fixture checks. Round 2 R6a: the phone goals store's rules.
 */
private val selfChecks: List<Pair<String, (CheckReport) -> Unit>> = listOf(
    "goals-store" to GoalListRulesCheck::run,
    // Round 2 R6b1: the readings list, the trend and the goal menu order (phone only: no C# twin).
    "goals-ui-page" to GoalsUiPageCheck::run,
    // Round 2 R6b2: New / Edit goal rules, areas, the weekly target's wait and the Goals settings defaults (phone only).
    "goals-ui-edit" to GoalsUiEditCheck::run,
    // The phone design-fix round: Today's goals, the order of an area's goals, notes and extra tiles, Start a session (phone only).
    "goals-ui-fix-round" to GoalsUiFixRoundCheck::run,
)

/** Areas whose every fixture file must have a check above (a new C# fixture file can't go unchecked). */
private val fullyCheckedAreas = listOf("goals", "goals-ui")

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

    for (area in fullyCheckedAreas.filter { onlyArea == null || it == onlyArea }) {
        val unchecked = FixtureFile.readArea(root, area).map { it.name }.filter { file -> checks.none { it.area == area && it.name == file } }
        report.check("$area: every fixture file has a check", unchecked.isEmpty(), "unchecked: $unchecked")
    }

    for ((area, check) in selfChecks.filter { onlyArea == null || it.first == onlyArea }) {
        runCatching { check(report) }.onFailure { report.check("$area: ran", false, "threw ${it::class.simpleName}: ${it.message}") }
    }

    report.printTotal()
    exitProcess(if (report.allPassed) 0 else 1)
}

private fun option(args: Array<String>, name: String): String? = args.indexOf(name).takeIf { it >= 0 && it + 1 < args.size }?.let { args[it + 1] }
