package com.accountabilibuddy.checks

import java.io.File
import kotlin.system.exitProcess

/** Every Kotlin parity check; add one per fixture file the C# checks write. */
private val checks: List<FixtureCheck> = listOf(
    PlannedTieTextCheck(),
    StepLettersCheck(),
    // Screen-time source v2 (ST1): guess, matching per platform, minutes, rule JSON, which device counts.
    ScreenTimeGuessCheck(),
    ScreenTimeMatchingCheck(),
    ScreenTimeMinutesCheck(),
    ScreenTimeRuleJsonCheck(),
    ScreenTimeCountsOnCheck(),
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
    // Round 2 R6b3: goal reminders and the weekly review on the phone against the PC (Core: reminder times, nudge, adjust; App: the
    // review's due rule, "Skip when done").
    GoalReminderTimesCheck(),
    GoalNudgeCheck(),
    GoalAdjustCheck(),
    GoalsUiReviewDueCheck(),
    GoalsUiReminderDoneCheck(),
    // Round 2 R6b4: Stats for one goal (its words, paused stretches and its year, hatched) against the PC's StatsGoalHistoryViewModel.
    GoalsUiStatsCheck(),
    // GS0 (ADR 79 forward compatibility): goal JSON of a newer app read and written back, unknowns kept after an edit, the area tidy rule.
    Gs0RoundTripCheck(),
    Gs0KeepCheck(),
    Gs0TidyCheck(),
    // Round 2 R7.1: the Kotlin task model and its merges against the PC's (ADR 76): JSON byte for byte (the PC -> phone -> PC round
    // trip of a task with every field set), field merge, local edits, labels, account settings, old fields, the migration rule.
    R71Check("task-json", R71ModelCases::taskJson),
    R71Check("values", R71ModelCases::values),
    R71Check("field-merge", R71MergeCases::fieldMerge),
    R71Check("task-edits", R71MergeCases::taskEdits),
    R71Check("label-merge", R71MergeCases::labelMerge),
    R71Check("account-settings", R71MergeCases::accountSettings),
    R71Check("lists-merge", R71MergeCases::listsMerge),
    R71Check("ring-devices", R71MergeCases::ringDevices),
    R71Check("numbering", R71MergeCases::numbering),
    R71Check("old-fields-write", R71OldFieldCases::write),
    R71Check("old-fields-json", R71OldFieldCases::json),
    R71Check("migration", R71OldFieldCases::migration),
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
    // Round 2 R6b3: the weekly review, goal reminders, sleep readings, the Deep work weekly carry item, trusted time (phone only).
    "goals-ui-review-reminders" to GoalsUiReviewRemindersCheck::run,
    // Round 2 R6b4: Stats per goal's chart (a bar per day, the target line, paused days hatched and left out of the numbers; phone only).
    "goals-ui-stats-series" to GoalsUiStatsSeriesCheck::run,
    // Round 2 R7.1: unknown fields and newer entries kept, the shared stamp helper, phone-only numbering, the label book, the migration's promises.
    "r71-self" to TaskModelSelfCheck::run,
)

/** Areas whose every fixture file must have a check above (a new C# fixture file can't go unchecked). */
private val fullyCheckedAreas = listOf("goals", "goals-ui", "gs0", "r71")

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
