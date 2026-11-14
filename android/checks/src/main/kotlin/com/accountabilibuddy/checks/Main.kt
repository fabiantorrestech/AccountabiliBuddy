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
    // Round 2 R7.2: the phone's task store against the PC's (normalize, History, the store's migration, Q128, Q583, strip, file parts).
    R72Check("normalize", R72Cases::normalize),
    R72Check("history", R72Cases::history),
    R72Check("store-migration", R72Cases::storeMigration),
    R72Check("old-fields-session", R72Cases::oldFieldsSession),
    R72Check("receive-without-tasks", R72Cases::receiveWithoutTasks),
    R72Check("strip", R72Cases::strip),
    R72Check("with-parts", R72Cases::withParts),
    // Round 2 R7.3a: what a view shows (filters, groups, sorts) against the PC's Core TaskViewRules, and the phone's page order
    // against the PC's column order (App TaskColumnOrder).
    R73Check("view-rules", R73Cases::viewRules),
    R73Check("column-order", R73Cases::columnOrder),
    // Round 2 R7.3b: the Add sheet's typed title, a reorder, the toasts' lives (the PC's own rules).
    R73Check("typed-title", R73bCases::typedTitle),
    R73Check("reorder", R73bCases::reorder),
    R73Check("toast-timing", R73bCases::toastTiming),
    // PT1: the search grammar (words, @label, #tag, a task number) against the PC's Core TaskSearch, and the view icons' names against the PC editor's.
    Pt1Check("search", Pt1Cases::search),
    Pt1Check("view-icons", Pt1Cases::viewIcons),
    // LU1-fix: the "listen fast" rule for lunch and the future-stamp clamp against the tables the PC asserts on its Core LunchListening.
    Lu1Check("listening", Lu1Cases::listening),
    Lu1Check("clamp", Lu1Cases::clamp),
    // LU1a (ADR 88): split lunch and discard: the day's rule, what a device takes from a file, equality, the delivery comparison, the per-mode setting and the lunch's JSON against the PC's Core.
    Lu1aCheck("ops", Lu1aCases::ops),
    Lu1aCheck("receive", Lu1aCases::receive),
    Lu1aCheck("equality", Lu1aCases::equality),
    Lu1aCheck("delivery", Lu1aCases::delivery),
    Lu1aCheck("setting", Lu1aCases::setting),
    Lu1aCheck("wire", Lu1aCases::wire),
    // AR1-fix: the Archive's rows (every source, the settle records, Recently deleted) against the PC's Core ArchiveBuilder.
    Ar1Check("archive-rows", Ar1Cases::rows),
    // AR1c: the Archive's notes (every device's, "from <device>", a removed device's stay) against the PC's Core ArchiveBuilder.
    Ar1Check("archive-notes", Ar1Cases::notes),
    // AR1c fix: the phone's list (order, tie order, step order, texts, summary, filters, search, Recently deleted) against the PC's real NotebookViewModel.
    Ar1Check("archive-list", Ar1Cases::list),
    // AR1a: session notes (ADR 87): the merge (newest edit, tombstones, window, clamp), which notes a device's own session shows, the lenient read and clean-up against Core's SessionNoteRules.
    Ar1aCheck("merge", Ar1aCases::merge),
    Ar1aCheck("between", Ar1aCases::between),
    Ar1aCheck("clean", Ar1aCases::clean),
    // LB1a: device-only labels and tags (ADR 89): the label merge with "Only on" by its own stamp, the tag scope merge, who sees what, the hidden
    // ones kept by every writer, the edits (add / join, set, name check, tag scope), the file part's clean-up and search against Core.
    Lb1aCheck("label-merge", Lb1aCases::labelMerge),
    Lb1aCheck("tag-scope-merge", Lb1aCases::tagScopeMerge),
    Lb1aCheck("visibility", Lb1aCases::visibility),
    Lb1aCheck("with-hidden", Lb1aCases::withHidden),
    Lb1aCheck("label-add", Lb1aCases::labelAdd),
    Lb1aCheck("label-set-only-on", Lb1aCases::labelSetOnlyOn),
    Lb1aCheck("check-name", Lb1aCases::checkName),
    Lb1aCheck("tag-scope-edits", Lb1aCases::tagScopeEdits),
    Lb1aCheck("clean-labels", R71ModelCases::taskJson),
    Lb1aCheck("clean-tag-scopes", Lb1aCases::cleanTagScopes),
    Lb1aCheck("search", Lb1aCases::search),
    // Round 2 GS2: the goals sync (ADR 79) against the PC's Core: field merge, edits, tombstones, Deep work, reviews, file parts (gs1a);
    // entry rows, derived values, counting across devices, the shared history (gs1b).
    Gs2Check("gs1a", "merge", Gs2Cases::merge),
    // GS2 review fix: "+" in a time (a known field is written straight, every other string as \u002B), equal edit times; the PC's wire text.
    Gs2Check("gs1a", "wire-plus", Gs2Cases::merge),
    Gs2Check("gs1a", "review-id", Gs2Cases::reviewId),
    Gs2Check("gs1a", "review-three", Gs2Cases::reviewThree),
    Gs2Check("gs1a", "deep-work", Gs2Cases::deepWork),
    Gs2Check("gs1a", "deep-work-split", Gs2Cases::deepWorkSplit),
    Gs2Check("gs1a", "edits", Gs2Cases::edits),
    Gs2Check("gs1a", "tombstones", Gs2Cases::tombstones),
    Gs2Check("gs1a", "parts", Gs2Cases::parts),
    Gs2Check("gs1b", "derived-id", Gs2Cases::derivedId),
    Gs2Check("gs1b", "rows", Gs2Cases::rows),
    Gs2Check("gs1b", "rows-read", Gs2Cases::rowsRead),
    Gs2Check("gs1b", "window", Gs2Cases::window),
    Gs2Check("gs1b", "writer", Gs2Cases::writer),
    Gs2Check("gs1b", "counting", Gs2Cases::counting),
    Gs2Check("gs1b", "merge", Gs2Cases::merge),
    Gs2Check("gs1b", "newer-derived-row", Gs2Cases::newerDerivedRow),
    Gs2Check("gs1b", "month-write", Gs2Cases::monthWrite),
    Gs2Check("gs1b", "to-read", Gs2Cases::toRead),
    Gs2Check("gs1b", "roll-up", Gs2Cases::rollUp),
    Gs2Check("gs1b", "may-remove", Gs2Cases::mayRemove),
    Gs2Check("gs1b", "roller", Gs2Cases::roller),
    Gs2Check("gs1b", "active", Gs2Cases::active),
    Gs2Check("gs1b", "converge", Gs2Cases::converge),
    Gs2Check("gs1b", "history-before-goal", Gs2Cases::historyBeforeGoal),
    // GS4 review: the history file names and the PC-sealed ABM1 / ABY1 files opened by the phone codec (the C# checks write gs4/*.json).
    Gs4Check("names"),
    Gs4Check("sealed"),
    // GS3b Kotlin relay mirror: the PC's quota / keep / version gate / late joiner / all-accounted rules (gs3bfix, one rule each) and the
    // plan over many devices and years, the late-joiner merge and more version texts (gs3bk).
    Gs3bkFixtureCheck("gs3bfix", "relay-quota"),
    Gs3bkFixtureCheck("gs3bfix", "relay-keep"),
    Gs3bkFixtureCheck("gs3bfix", "knows-history"),
    Gs3bkFixtureCheck("gs3bfix", "late-prunable"),
    Gs3bkFixtureCheck("gs3bfix", "all-accounted"),
    Gs3bkFixtureCheck("gs3bk", "relay-plan"),
    Gs3bkFixtureCheck("gs3bk", "late-merge"),
    Gs3bkFixtureCheck("gs3bk", "version-gate"),
    // SYNC1a (ADR 86): receipts in sealed files, the read-failure reasons, the colour rule, the receiver-local record, "Keep both".
    Sync1aCheck("receipts-file"),
    Sync1aCheck("board"),
    Sync1aCheck("local"),
    Sync1aCheck("keep-both"),
    Sync1aCheck("backup"),
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
    // Round 2 R7.2: the phone's own edits, label edits, the switch's refusal, newer entries, timing (phone only).
    "r72-self" to TaskStoreSelfCheck::run,
    // R7.2 review fixes: the store's start and migration decisions against fakes of its prefs (unreadable stores, the backup, the
    // set-aside copy), numbering that waits for a PC, the notice for a PC without `tasks`, Add to Today by content (phone only).
    "r72-fix" to R72FixSelfCheck::run,
    // Round 2 R7.3a: the Shelf's paging, pages, the Today strip (words, counts, the drop into Queued), drops on label and tag pages,
    // the page's stamped edits, a row's words (phone only).
    "r73a-self" to TasksPageSelfCheck::run,
    // Round 2 R7.3b: what a drag's release does where, the selection, stacks, the picker's tiles, reordering, the session cap's swap,
    // Undo, the toast's and the Add sheet's words, "Rings on" for this phone, the "Update AccountabiliBuddy on" notice (phone only).
    "r73b-self" to TasksDragSelfCheck::run,
    // PT1 (phone): the Group by pills, the view icons, the search hint and a view's search through the shared rule.
    "pt1-self" to Pt1SelfCheck::run,
    // LU1-fix: the shared "listen fast" rule for lunch (running here or in a received file, or due) and the future-stamp clamp.
    "lu1-self" to Lu1SelfCheck::run,
    // LU1a: an older file reads as unknown pieces, a lunch from an older app keeps the counted pieces, the delivery line.
    "lu1a-self" to Lu1aSelfCheck::run,
    // LB1a (phone): an older file leaves the scopes alone, the part merges, is written last and never in a QR file or the kept copy, the store document.
    "lb1a-self" to Lb1aSelfCheck::run,
    // LU1c: the lunch X on the phone and the watch (Count it / Discard it / Keep going, Start the rest, the lock per piece, the watch snapshot).
    "lu1c-self" to Lu1cSelfCheck::run,
    // AR1c (phone): the Archive page's list (order, filters, search, 12-hour times, Recently deleted), notes edit/delete/merge, settle-record restore,
    // notes out of the watch, plain backups and logs, the UI rules of the new page (phone only; no PC twin for the Compose code).
    "ar1c-self" to Ar1cSelfCheck::run,
    // PH1 + TD1 (phone): the day panel's one height, every Today part hideable in both layouts, More today bar show / hide, the steps part
    // gone with old layouts read fine, the one-time "Steps now live in Goals" card (phone only).
    "ph1td1-self" to Ph1Td1SelfCheck::run,
    // Round 2 GS4: the phone's goals sync wiring: the weekly receive rule, stamping, derived ownership, what goes out, the history transport (phone only).
    "gs4-self" to Gs4SelfCheck::run,
    // GS4 review fixes: the fail-safe default environment, apply results, the one history lock under threads, names, wiring (phone only).
    "gs4fix-self" to Gs4FixSelfCheck::run,
    // GS3b Kotlin relay mirror (phone): the relay history pass on a fake relay: open months only, quota, 429, free places, version gate, 25 days, late joiner.
    "gs3bk-self" to Gs3bkSelfCheck::run,
    // Round 2 GS2 review fixes: "+" in a time, non-finite numbers, Health Connect's highest, the small reading rules, a 65 000-entry timing check (phone only).
    "gs2fix-self" to Gs2FixSelfCheck::run,
    // Round 2 R7.4 + NG1 (phone): account-settings edits, label colours and rings, reminder/due/device edits, Hide the Shelf, Rebuild, the
    // phone-owned Deep work daily target and the no-PC words (phone only).
    "r74-self" to R74SelfCheck::run,
    // Round 2 R7.5 + HM1 (phone): the tablet sizes of the phone Tasks layout and the heatmaps' month lines (phone only).
    "r75-self" to R75SelfCheck::run,
    // Phone sync bug: goals and tasks travel both ways (phone only).
    "phonesync1" to Phonesync1Check::run,
    // SYNC1c (phone sync troubleshooting): the board, the send log, what each part came to, the unreadable list, the stale-file rule, the paced
    // relay re-read, the log and the report with no private text, the conflict picker (phone only; the picker's parity runs in sync1a/keep-both).
    "sync1c" to Sync1cCheck::run,
)

/** Areas whose every fixture file must have a check above (a new C# fixture file can't go unchecked). */
private val fullyCheckedAreas = listOf("goals", "goals-ui", "gs0", "gs1a", "gs1b", "r71", "r72", "r73", "gs4", "sync1a", "gs3bfix", "gs3bk", "pt1", "ar1", "lu1", "lu1a", "ar1a", "lb1")

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
