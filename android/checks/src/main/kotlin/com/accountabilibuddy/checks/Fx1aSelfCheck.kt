package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.data.BackupFile
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.notify.GoalReminderIds
import com.accountabilibuddy.companion.ui.goals.GoalEditorDirty
import com.accountabilibuddy.companion.ui.sheets.SheetDiscardRules
import com.accountabilibuddy.companion.ui.tasks.page.AddTaskDirty
import com.accountabilibuddy.shared.goals.GoalApplyResult
import com.accountabilibuddy.shared.goals.GoalDraftText
import com.accountabilibuddy.shared.goals.GoalDrafts
import com.accountabilibuddy.shared.goals.GoalEntry
import com.accountabilibuddy.shared.goals.GoalEntrySource
import com.accountabilibuddy.shared.goals.GoalHistoryDeviceInfo
import com.accountabilibuddy.shared.goals.GoalHistoryFileKey
import com.accountabilibuddy.shared.goals.GoalHistoryNames
import com.accountabilibuddy.shared.goals.GoalHistoryStore
import com.accountabilibuddy.shared.goals.GoalHistorySync
import com.accountabilibuddy.shared.goals.GoalJson
import com.accountabilibuddy.shared.goals.GoalLists
import com.accountabilibuddy.shared.goals.GoalReminder
import com.accountabilibuddy.shared.goals.GoalReminderDays
import com.accountabilibuddy.shared.goals.GoalReminderMark
import com.accountabilibuddy.shared.goals.GoalReminderMarkKind
import com.accountabilibuddy.shared.goals.GoalReminderPlan
import com.accountabilibuddy.shared.goals.GoalReminderState
import com.accountabilibuddy.shared.goals.GoalReminderTimes
import com.accountabilibuddy.shared.goals.GoalReviewRules
import com.accountabilibuddy.shared.goals.GoalReviewSnooze
import com.accountabilibuddy.shared.goals.GoalSeeds
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalWeekStart
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import java.io.File
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.Locale
import java.util.UUID

/**
 * FX-A, the phone fix round: the pure decisions of the fixes and the structure of the parts that need a device.
 * Leaving the group (NG1: the phone's own history files leave the sync folder), the atomic folder write, the clean backup text (Q880).
 */
object Fx1aSelfCheck {
    private const val GROUP = "0a1b2c3d"
    private val ME = UUID.fromString("aaaaaaaa-0000-0000-0000-000000000001")
    private val T0 = Gs4SelfCheck.T0

    fun run(report: CheckReport) {
        leaving(report)
        folderStore(report)
        backupText(report)
        discard(report)
        guardsAndNotifications(report)
        oldBackup(report)
        reviewMinutes(report)
        alarm(report)
        notifications(report)
        clock(report)
        snooze(report)
    }

    // ---------------------------------------------------------------------------------------------- unsaved changes (item 1, finding 1)

    private fun discard(report: CheckReport) {
        val id = UUID.fromString("99999999-0000-0000-0000-000000000001")
        val fresh = GoalDrafts.newDraft(id, GoalSeeds.WORK_AREA_ID)
        report.check("fx1a/discard: a new goal opened and left alone holds nothing to lose", !GoalEditorDirty.isDirty(fresh, fresh, emptyList(), emptyList(), true to true, true to true))
        report.check("fx1a/discard: a typed name is something to lose", GoalEditorDirty.isDirty(fresh.copy(name = "Read"), fresh, emptyList(), emptyList(), true to true, true to true))
        val rest = GoalEditorDirty.isDirty(fresh, fresh, listOf(reminder()), emptyList(), true to true, true to true)
        report.check("fx1a/discard: an added reminder is something to lose", rest)
        report.check("fx1a/discard: a changed reminder rule is something to lose", GoalEditorDirty.isDirty(fresh, fresh, emptyList(), emptyList(), false to true, true to true))
        val restored = GoalDraftText.read(GoalDraftText.write(fresh.copy(name = "Read")))
        report.check("fx1a/discard: a draft that went through a screen turn compares equal to itself", restored == fresh.copy(name = "Read"))
        report.check("fx1a/discard: the question for a new goal and for an edit", GoalEditorDirty.question(true) == "Discard this goal?" && GoalEditorDirty.question(false) == "Discard your changes?")
        report.check(
            "fx1a/discard: a sheet refuses to close only when guarded and dirty",
            SheetDiscardRules.refuses(true, true) && !SheetDiscardRules.refuses(true, false) && !SheetDiscardRules.refuses(false, true) && !SheetDiscardRules.refuses(false, false),
        )
        val sheet = Sync1cCheck.appSource("ui/sheets/AbSheet.kt")?.let(Sync1cCheck::code)
        val body = sheet?.let { Sync1cCheck.body(it, "    val guard by rememberUpdatedState", "        shape = RoundedCornerShape(topStart") }
        report.check(
            "fx1a/discard: the sheet vetoes Hidden while guarded and dirty, asks, and brings a hidden sheet back",
            body != null && body.contains("confirmValueChange") && body.contains("SheetValue.Hidden") && body.contains("held.ask()") && body.contains("sheetState.show()") && body.contains("onDismiss()"),
        )
        val editor = Sync1cCheck.appSource("ui/goals/GoalEditorSheets.kt")?.let(Sync1cCheck::code)
        report.check(
            "fx1a/discard: every sheet of the goal editor flow sits under the guard, the new-area sheet under none",
            editor != null && Regex("ProvideSheetDiscardGuard\\(guard\\)").findAll(editor).count() == 4 && editor.contains("ProvideSheetDiscardGuard(null)") && editor.contains("DiscardChangesDialog(GoalEditorDirty.question(goalId == null)"),
        )
        val add = Sync1cCheck.appSource("ui/tasks/page/AddTaskSheet.kt")?.let(Sync1cCheck::code)
        report.check(
            "fx1a/discard: the Add a task sheet asks \"Discard this task?\" when something is typed",
            add != null && add.contains("discardGuard = SheetDiscardGuard(dirty)") && add.contains("DiscardChangesDialog(\"Discard this task?\"") && add.contains("AddTaskDirty.isDirty("),
        )
    }

    // ---------------------------------------------------------------------------------------------- review B1, S1, S2, S3, N4, N6

    private fun guardsAndNotifications(report: CheckReport) {
        // B1: a page that rests in a session must read the shell's lock (LocalGuardsLock), not LocalGuardsLocked, which only a GuardsScope sets.
        val root = listOf(File("app/src/main/java/com/accountabilibuddy/companion/ui"), File("../app/src/main/java/com/accountabilibuddy/companion/ui")).firstOrNull { it.isDirectory }
        val outside = root?.walkTopDown()?.filter { it.isFile && it.extension == "kt" }?.filter { file ->
            val text = file.readText()
            // Reading it is fine inside a GuardsScope (the file wraps its section in one), or in the kit controls that only live inside one.
            text.contains("LocalGuardsLocked.current") && !text.contains("GuardsScope") && file.name !in setOf("GuardControls.kt", "SettingsParts.kt", "ReenableTimes.kt")
        }?.map { it.name }?.toList()
        report.check("fx1a/locks: no page reads LocalGuardsLocked outside a GuardsScope (the settings pages and the setup page use LocalGuardsLock)", outside != null && outside.isEmpty(), "$outside")
        for (page in listOf("ui/settings/GoalsSettingsPage.kt", "ui/settings/TasksSettingsPage.kt", "ui/GoalsReviewSetupPage.kt")) {
            val text = Sync1cCheck.appSource(page)?.let(Sync1cCheck::code)
            report.check("fx1a/locks: $page rests while a session runs", text != null && text.contains("LocalGuardsLock.current != null") && !text.contains("LocalGuardsLocked.current"))
        }

        // S1 and S2: Back is taken over while there is something to lose; the question is not restored behind the sheet after a turn.
        val sheet = Sync1cCheck.appSource("ui/sheets/AbSheet.kt")?.let(Sync1cCheck::code)
        report.check(
            "fx1a/discard: S1 while dirty Back is the sheet's own question (no hide to leave it shrunk)",
            sheet != null && sheet.contains("shouldDismissOnBackPress = !refusing") && sheet.contains("BackHandler(enabled = refusing)"),
        )
        val editor = Sync1cCheck.appSource("ui/goals/GoalEditorSheets.kt")?.let(Sync1cCheck::code)
        report.check(
            "fx1a/discard: S2 the question is not saved across a screen turn; the draft is",
            editor != null && editor.contains("var askDiscard by remember(goalId)") && !editor.contains("askDiscard by rememberSaveable") && editor.contains("var draft by rememberSaveable"),
        )
        report.check(
            "fx1a/discard: N4 the review's Adjust is already in the start draft: not a change until typed",
            editor != null && editor.contains("GoalDrafts.withAdjustment(GoalDrafts.fromGoal(stored, weekly.minutes), suggested) else"),
        )
        fun dirty(title: String = "", step: String = "", steps: List<String> = emptyList(), note: String = "", due: Boolean = false, limit: Boolean = false, label: Boolean = false, tags: Boolean = false, rules: Boolean = false, here: Boolean = false) =
            AddTaskDirty.isDirty(title, step, steps, note, due, limit, label, tags, rules, here)
        report.check("fx1a/discard: N6 an untouched Add a task sheet is clean; a title, step, note, date, limit, label, tag, rule or 'only here' is something to lose",
            !dirty() && dirty(title = "x") && dirty(step = "x") && dirty(steps = listOf("a")) && dirty(note = "n") && dirty(due = true) && dirty(limit = true) &&
                dirty(label = true) && dirty(tags = true) && dirty(rules = true) && dirty(here = true) && !dirty(title = "  "))

        // S3: the goal's name is in the text, so none of the goal notifications bridges to a watch.
        val source = Sync1cCheck.appSource("notify/GoalReminders.kt")?.let(Sync1cCheck::code)
        report.check("fx1a/notify: S3 the reminder, its public version and the review are local only", source != null && Regex("""\.setLocalOnly\(true\)""").findAll(source).count() == 3)
    }

    // The files of builds before FX-A wrote `toString` (the internal time mark as \u0001 in the text): they still open and read.
    private fun oldBackup(report: CheckReport) {
        val goal = Gs4SelfCheck.goal(1)
        val content: JsonObject = buildJsonObject { put("goals", buildJsonObject { put("goals", GoalJson.writeGoals(listOf(goal))) }) }
        val old = buildJsonObject {
            put("format", "accountabilibuddy-backup")
            put("version", 1)
            put("protection", "None")
            put("content", content)
        }.toString()
        report.check("fx1a/backup: an old-format file really carries the mark (so the restore check below means something)", old.contains("\\u0001"))
        val read = runCatching { GoalJson.readGoals((BackupFile.open(old)["goals"] as JsonObject)["goals"] as JsonArray) }.getOrNull()
        report.check("fx1a/backup: an old (marked) backup still opens and restores the goal", read?.map { it.id } == listOf(goal.id) && read[0].createdAt == goal.createdAt, "$read")
    }

    private fun reminder() = GoalReminder(UUID.fromString("00000000-0000-0000-0000-000000000701"), UUID.fromString("99999999-0000-0000-0000-000000000001"), GoalReminderDays.EveryDay, LocalTime.of(20, 0), 1.0, T0)

    // ---------------------------------------------------------------------------------------------- finding 3: "About 1 minutes"

    private fun reviewMinutes(report: CheckReport) {
        report.check("fx1a/minutes: 1 goal reads \"1 minute\", 8 goals \"4 minutes\"", GoalReviewRules.minutesText(1) == "1 minute" && GoalReviewRules.minutesText(2) == "1 minute" && GoalReviewRules.minutesText(8) == "4 minutes")
        val one = Gs4SelfCheck.goal(1)
        val row = GoalReviewRules.row(true, false, listOf(one), null, T0.toInstant(), false, java.time.DayOfWeek.SUNDAY)
        report.check("fx1a/minutes: the card says \"About 1 minute · Sundays\" for one goal", row.note == "About 1 minute · Sundays", row.note)
        report.check("fx1a/minutes: with four goals \"About 2 minutes\"", GoalReviewRules.row(true, false, List(4) { Gs4SelfCheck.goal(1, name = "g$it") }, null, T0.toInstant(), false, java.time.DayOfWeek.SUNDAY).note == "About 2 minutes · Sundays")
        val reminders = Sync1cCheck.appSource("notify/GoalReminders.kt")?.let(Sync1cCheck::code)
        report.check("fx1a/minutes: the review's notification uses the same words", reminders != null && reminders.contains("GoalReviewRules.minutesText(moment.goals.size)") && !reminders.contains("minutesFor(moment.goals.size)} minutes"))
    }

    // ---------------------------------------------------------------------------------------------- R6b3 review S1: the alarm for tomorrow

    private fun alarm(report: CheckReport) {
        val zone = ZoneOffset.ofHours(-5)
        fun at(day: LocalDate, hour: Int, minute: Int = 0) = OffsetDateTime.of(day.atTime(hour, minute), zone)
        val today = LocalDate.of(2026, 11, 11)
        val made = at(today.minusDays(60), 8)
        val goal = Gs4SelfCheck.goal(1).copy(createdAt = made, editedAt = made)
        val noon = GoalReminder(UUID.fromString("00000000-0000-0000-0000-000000000711"), goal.id, GoalReminderDays.EveryDay, LocalTime.of(20, 0), 1.0, made)
        val data = GoalSourceData(emptyList(), emptyList(), zone)
        fun step(now: OffsetDateTime, list: List<GoalReminder>, state: GoalReminderState = GoalReminderState.EMPTY) =
            GoalReminderPlan.step(list, listOf(goal), state, data, GoalWeekStart.Sunday, now.toInstant(), zone, false)

        // After the day's last reminder has rung there was no "next" at all: the alarm went off and only the inexact refresh could ring tomorrow's.
        val rung = GoalReminderState.EMPTY.with(GoalReminderPlan.reminderKey(noon.id, at(today, 20).toInstant()), GoalReminderMark(GoalReminderMarkKind.Shown, at(today, 20, 1).toInstant()), at(today, 20, 1).toInstant())
        val night = step(at(today, 21), listOf(noon), rung)
        report.check("fx1a/alarm: after today's last reminder the next moment is tomorrow's first (8:00 PM), or midnight if sooner", night.nextAt == at(today.plusDays(1), 0).toInstant(), "${night.nextAt}")
        val morning = step(at(today.plusDays(1), 0, 5), listOf(noon), rung)
        report.check("fx1a/alarm: after midnight it is tomorrow's reminder at 8:00 PM", morning.nextAt == at(today.plusDays(1), 20).toInstant(), "${morning.nextAt}")
        val early = GoalReminder(UUID.fromString("00000000-0000-0000-0000-000000000712"), goal.id, GoalReminderDays.EveryDay, LocalTime.of(6, 30), 2.0, made)
        report.check("fx1a/alarm: tomorrow's earliest reminder is a candidate", step(at(today, 21), listOf(early), GoalReminderState.EMPTY).nextAt == at(today.plusDays(1), 0).toInstant())
        report.check("fx1a/alarm: nothing live, nothing to wake for", step(at(today, 21), emptyList()).nextAt == null)
        val source = Sync1cCheck.appSource("notify/GoalReminders.kt")?.let(Sync1cCheck::code)
        val review = source?.let { Sync1cCheck.body(it, "private fun reviewNotification", "private fun post(") }
        report.check(
            "fx1a/alarm: the review's next moment is a week after its own (S1), and a moment the phone slept through doesn't ring late (S3)",
            review != null && review.contains("plusDays(7).toInstant()") && review.contains("Duration.between(ringAt, now) > GoalReminderPlan.LOOKBACK"),
        )
    }

    // ---------------------------------------------------------------------------------------------- R6b3 review S2, S4, the NITs, Q549

    private fun notifications(report: CheckReport) {
        val source = Sync1cCheck.appSource("notify/GoalReminders.kt")?.let(Sync1cCheck::code)
        report.check("fx1a/notify: the source was found", source != null)
        source ?: return
        report.check("fx1a/notify: S2 refresh is synchronized", Regex("""@Synchronized\s+fun refresh\(""").containsMatchIn(source))
        val post = Sync1cCheck.body(source, "private fun post(", "private fun statusText") ?: ""
        report.check("fx1a/notify: S2 a goal reminder alerts once", post.contains(".setOnlyAlertOnce(true)"))
        report.check("fx1a/notify: Q549 the reminder names the goal, stays private, and its public version has no name", post.contains("\"${'$'}{goal.name} · ") && post.contains("VISIBILITY_PRIVATE") && post.contains(".setPublicVersion(public)") && !Sync1cCheck.body(post, "val public =", "val builder")!!.contains("goal.name"))
        report.check("fx1a/notify: Q549 no reason or note reaches a notification", !post.contains("reason") && !post.contains("notes"))
        report.check("fx1a/notify: a snooze keeps its count when the occurrence is shown again", source.contains("snoozeCount = occurrence.snoozeCount"))
        val snooze = Sync1cCheck.body(source, "fun snoozeReview(", "fun cancelReview") ?: ""
        report.check(
            "fx1a/notify: the notification's review Snooze takes Settings' length and cancels nothing when nothing was saved",
            snooze.contains("snoozeDays == 2") && snooze.contains("if (PhoneGoalReview.snooze(app, choice) == null)") && !snooze.contains("cancel("),
        )
        val review = Sync1cCheck.appSource("goals/PhoneGoalReview.kt")?.let(Sync1cCheck::code)
        report.check("fx1a/notify: the card's own snooze takes the posted notification down", review != null && review.contains("GoalReminders.cancelReview(app)") && review.contains("if (error == null)"))
        val setup = Sync1cCheck.appSource("ui/GoalsReviewSetupPage.kt")?.let(Sync1cCheck::code)
        report.check("fx1a/notify: S4 the review-day chips rest in a session", setup != null && setup.contains("LocalGuardsLock.current != null") && !setup.contains("LocalGuardsLocked") && setup.contains("enabled = !locked") && setup.contains("if (!locked) GoalSettingsPrefs.setReview"))

        // The ids: the four intents of a reminder never share a request code, nor with another reminder's, nor with the review's or the alarm's.
        val keys = (0 until 20000).map { "r:00000000-0000-0000-0000-%012d:%d".format(it % 7, 1_760_000_000L + it * 86_400L) }.distinct()
        val codes = HashMap<Int, String>()
        var clash = 0
        var bucketsSeen = HashMap<Int, String>()
        for (key in keys) {
            val bucket = GoalReminderIds.bucket(key)
            val owner = bucketsSeen.put(bucket, key)
            if (owner != null) continue // a repeated bucket is one reminder's (hash) collision, not a code clash within a bucket
            for (family in GoalReminderIds.Family.entries) if (codes.put(GoalReminderIds.code(key, family), key + family) != null) clash++
        }
        report.check("fx1a/ids: ${keys.size} reminders (those in different buckets), four intents each: no two share a request code", clash == 0, "$clash clashes")
        report.check(
            "fx1a/ids: the four intents of one reminder are four different codes, apart from the review's and the alarm's",
            GoalReminderIds.Family.entries.map { GoalReminderIds.code(keys[0], it) }.toSet().size == 4 &&
                keys.take(2000).all { key -> GoalReminderIds.notificationId(key) > GoalReminderIds.REVIEW_SNOOZE_CODE && GoalReminderIds.Family.entries.all { GoalReminderIds.code(key, it) > 41 && GoalReminderIds.code(key, it) != GoalReminderIds.notificationId(key) } },
        )
    }

    // ---------------------------------------------------------------------------------------------- finding: "4:00 pm"

    private fun clock(report: CheckReport) {
        val before = Locale.getDefault()
        try {
            val noon = LocalDateTime.of(2026, 11, 15, 16, 0)
            val seen = listOf(Locale.UK, Locale.forLanguageTag("en-AU"), Locale.forLanguageTag("en-IN"), Locale.GERMANY, Locale.US).map { locale ->
                Locale.setDefault(locale)
                ClockFormat(is24Hour = false).time(noon) to ClockFormat(is24Hour = false).time(noon.withHour(9))
            }
            report.check("fx1a/clock: 12-hour times read \"4:00 PM\" / \"9:00 AM\" whatever language the phone is set to", seen.all { it == ("4:00 PM" to "9:00 AM") }, "$seen")
        } finally {
            Locale.setDefault(before)
        }
        val sources = listOf("backup/AutoBackup.kt", "sync/SleepSettingsSync.kt", "ui/PlacementSection.kt", "stats/StatsFormat.kt", "engine/ClockFormat.kt")
        val loose = sources.filter { path ->
            val text = Sync1cCheck.appSource(path)?.let(Sync1cCheck::code) ?: return@filter true
            Regex("""ofPattern\("[^"]*\ba\b[^"]*"\)""").containsMatchIn(text) || (path == "stats/StatsFormat.kt" && text.contains("\"h:mm a\", locale)"))
        }
        report.check("fx1a/clock: no 12-hour pattern of the phone takes the system's locale", loose.isEmpty(), "$loose")
        val watch = File("../shared/src/main/java/com/accountabilibuddy/shared/WatchText.kt").takeIf { it.isFile }?.readText().orEmpty()
        report.check("fx1a/clock: the watch's twelve-hour text is fixed too", watch.contains("ofPattern(\"h:mm a\", java.util.Locale.US)"))
    }

    // ---------------------------------------------------------------------------------------------- Q548 the PB snooze

    private fun snooze(report: CheckReport) {
        val zone = ZoneOffset.ofHours(-5)
        val due = OffsetDateTime.of(LocalDate.of(2026, 11, 8).atTime(18, 0), zone)
        val now = OffsetDateTime.of(LocalDate.of(2026, 11, 11).atTime(19, 0), zone).toInstant()
        val pb = GoalReviewRules.snoozeUntil(GoalReviewSnooze.Tomorrow, true, due, now, zone)
        report.check("fx1a/snooze: ProductaBuddy: 9:00 AM on next week's review day, whatever the review's own time", pb == OffsetDateTime.of(LocalDate.of(2026, 11, 15).atTime(9, 0), zone), "$pb")
        val ab = GoalReviewRules.snoozeUntil(GoalReviewSnooze.Tomorrow, false, due, now, zone)
        val ab2 = GoalReviewRules.snoozeUntil(GoalReviewSnooze.TwoDays, false, due, now, zone)
        report.check(
            "fx1a/snooze: AccountabiliBuddy: 9:00 AM one day later, or two (Settings' choice); \"Remind me once\" is one day",
            ab == OffsetDateTime.of(LocalDate.of(2026, 11, 12).atTime(9, 0), zone) && ab2 == OffsetDateTime.of(LocalDate.of(2026, 11, 13).atTime(9, 0), zone) &&
                GoalReviewRules.snoozeUntil(GoalReviewSnooze.RemindOnce, false, due, now, zone) == ab,
        )
    }

    // ---------------------------------------------------------------------------------------------- leaving the group (item 3)

    private fun leaving(report: CheckReport) {
        val entry = GoalEntry(UUID.fromString("77777777-0000-0000-0000-000000000001"), Gs4SelfCheck.goal(1).id, LocalDate.of(2026, 11, 14), null, 1.0, GoalEntrySource.Hand, T0)
        var lists = GoalLists(listOf(GoalSeeds.workArea(T0)), listOf(GoalSeeds.deepWork(T0), Gs4SelfCheck.goal(1)), listOf(entry))
        var key: GoalHistorySync.GoalHistoryKey? = GoalHistorySync.GoalHistoryKey(GROUP, "fp")
        val sync = GoalHistorySync(
            held = { lists }, applySynced = { merge -> Gs4SelfCheck.apply({ lists }, { lists = it }, merge) }, states = Gs4SelfCheck.Holder(), now = { T0 }, self = { ME },
            key = { key }, codec = Gs4SelfCheck.FakeCodec, devices = { emptyList<GoalHistoryDeviceInfo>() }, enabled = { true }, ready = { true },
        )
        val folder = Gs4SelfCheck.Fake("folder", ".abgh", expires = false, canDelete = true)
        sync.run(folder)
        val own = sync.ownNames("folder")
        report.check("fx1a/leave: the pass put this phone's history file in the folder and remembers its name", own.isNotEmpty() && own.all { it in folder.files })

        // Another device's file and a regular sync file share the folder: they are never touched.
        val foreign = GoalHistoryNames.nameFor(GROUP, GoalHistoryFileKey.forMonth(UUID.fromString("bbbbbbbb-0000-0000-0000-000000000002"), 2026, 10), ".abgh")
        folder.files[foreign] = ByteArray(4)
        folder.files["0123456789ab.absync"] = ByteArray(4)

        key = null
        val ownCount = own.size
        val gone = sync.leave(folder)
        report.check("fx1a/leave: leaving deletes this phone's own history files from the folder", gone == ownCount && own.none { it in folder.files })
        report.check("fx1a/leave: another device's history file and the sync files stay", foreign in folder.files && "0123456789ab.absync" in folder.files)
        report.check("fx1a/leave: the folder's state is forgotten", sync.ownNames("folder").isEmpty())

        // A store that never deletes (the relay) is left alone: its files expire, and go with its sync file.
        val relay = Gs4SelfCheck.Fake("relay", ".absync", expires = true, canDelete = false, maxFiles = 64)
        key = GoalHistorySync.GoalHistoryKey(GROUP, "fp")
        sync.run(relay)
        val relayFiles = relay.files.keys.toList()
        report.check("fx1a/leave: a store that never deletes keeps its files and state", sync.leave(relay) == 0 && relay.files.keys.toList() == relayFiles && sync.ownNames("relay").isNotEmpty())

        // A folder that refuses a delete keeps its state, so the next key's clean-up still finds the file.
        val inner = Gs4SelfCheck.Fake("folder", ".abgh", expires = false, canDelete = true)
        val stubborn = object : GoalHistoryStore by inner {
            override fun delete(name: String) = false
        }
        sync.run(stubborn)
        report.check("fx1a/leave: a delete the folder refuses keeps the state", sync.leave(stubborn) == 0 && sync.ownNames("folder").isNotEmpty())

        // Nothing was put here: nothing to do.
        val fresh = GoalHistorySync(
            held = { lists }, applySynced = { GoalApplyResult.Unchanged }, states = Gs4SelfCheck.Holder(), now = { T0 }, self = { ME },
            key = { key }, codec = Gs4SelfCheck.FakeCodec, devices = { emptyList() }, enabled = { true }, ready = { true },
        )
        // Review S4: a name the listing doesn't show (a provider's partial listing) is not "gone": nothing is forgotten.
        key = GoalHistorySync.GoalHistoryKey(GROUP, "fp")
        val partial = Gs4SelfCheck.Fake("folder", ".abgh", expires = false, canDelete = true)
        val partialSync = GoalHistorySync(
            held = { lists }, applySynced = { merge -> Gs4SelfCheck.apply({ lists }, { lists = it }, merge) }, states = Gs4SelfCheck.Holder(), now = { T0 }, self = { ME },
            key = { key }, codec = Gs4SelfCheck.FakeCodec, devices = { emptyList<GoalHistoryDeviceInfo>() }, enabled = { true }, ready = { true },
        )
        partialSync.run(partial)
        val written = partialSync.ownNames("folder")
        val shown = object : GoalHistoryStore by partial {
            override fun list() = emptyList<com.accountabilibuddy.shared.goals.GoalHistoryRemoteFile>()
        }
        report.check(
            "fx1a/leave: S4 a partial (empty) listing deletes nothing and keeps the state, so the next key's clean-up still finds the files",
            written.isNotEmpty() && partialSync.leave(shown) == 0 && partialSync.ownNames("folder") == written && written.all { it in partial.files },
        )
        report.check("fx1a/leave: with no history files of this phone it does nothing", fresh.leave(Gs4SelfCheck.Fake("folder", ".abgh", expires = false, canDelete = true)) == 0)
    }

    // ---------------------------------------------------------------------------------------------- the folder store (item 3)

    private fun folderStore(report: CheckReport) {
        val store = Sync1cCheck.appSource("sync/GoalHistoryStores.kt")?.let(Sync1cCheck::code)
        val folder = store?.let { Sync1cCheck.body(it, "class FolderGoalHistoryStore", "private fun supportsDelete") }
        val put = folder?.let { Sync1cCheck.body(it, "override fun put(", "override fun delete(") }
        report.check("fx1a/folder-store: the source was found", put != null)
        put ?: return
        report.check("fx1a/folder-store: a put writes to a .tmp file first", put.contains("TEMPORARY_SUFFIX") && put.indexOf("createDocument") < put.indexOf("renameDocument"))
        report.check("fx1a/folder-store: the .tmp file is moved over the target by a rename", put.contains("renameDocument(resolver, temporary, name)"))
        report.check("fx1a/folder-store: a failed write deletes the .tmp file and rethrows", put.contains("catch (e: Exception)") && put.contains("deleteDocument(resolver, temporary)") && put.contains("throw e"))
        report.check("fx1a/folder-store: a leftover .tmp file is cleared before a new write", put.contains("uris[temporaryName]?.let"))
        val source = Sync1cCheck.appSource("sync/DriveFolderSource.kt")?.let(Sync1cCheck::code)
        val leave = source?.let { Sync1cCheck.body(it, "fun leaveGroup()", "fun schedule()") }
        report.check(
            "fx1a/folder-store: the folder source has leaveGroup, which runs the history leave on the folder store under the pass lock",
            leave != null && leave.contains("history.leave(FolderGoalHistoryStore(context, tree))") && leave.contains("running.withLock"),
        )
        val vm = Sync1cCheck.appSource("ui/CompanionViewModel.kt")?.let(Sync1cCheck::code)
        val leaving = vm?.let { Sync1cCheck.body(it, "fun leaveSyncGroup()", "fun shareSyncFiles") }
        report.check(
            "fx1a/folder-store: leaving the group asks the folder source first, while the key is still here",
            leaving != null && leaving.contains("folderSource.leaveGroup()") && leaving.indexOf("folderSource.leaveGroup()") < leaving.indexOf("sync.leave()"),
        )
    }

    // ---------------------------------------------------------------------------------------------- backup text (item 4, Q880)

    private fun backupText(report: CheckReport) {
        val goal = Gs4SelfCheck.goal(1)
        val content: JsonObject = buildJsonObject {
            put("kind", "tasks")
            put("goals", buildJsonObject { put("goals", GoalJson.writeGoals(listOf(goal))) })
        }
        val mark = '\u0001'
        // `toString` writes the mark as the six characters \u0001, the way the PC's file would have shown it.
        val marked = "\\u0001"
        report.check("fx1a/backup: the goal JSON tree does carry the internal time mark (so the checks below mean something)", content.toString().contains(marked))
        val plain = BackupFile.seal(content)
        report.check("fx1a/backup: a backup without a password has no internal mark in its text", !plain.contains(mark) && !plain.contains(marked))
        val read = runCatching { GoalJson.readGoals((BackupFile.open(plain)["goals"] as JsonObject)["goals"] as JsonArray) }.getOrNull()
        report.check("fx1a/backup: it opens and still reads as the same goal", read?.map { it.id } == listOf(goal.id))
        val sealed = BackupFile.seal(content, "a password")
        val opened = runCatching { BackupFile.open(sealed, "a password") }.getOrNull()
        report.check("fx1a/backup: a backup with a password opens too", opened != null && opened["kind"].toString() == "\"tasks\"")
        val inside = runCatching {
            // The decrypted text itself, before any parsing: the marks would show here.
            val method = BackupFile::class.java.getDeclaredMethod("decrypt", JsonObject::class.java, String::class.java).also { it.isAccessible = true }
            method.invoke(BackupFile, Json.parseToJsonElement(sealed) as JsonObject, "a password") as String
        }.getOrNull()
        report.check("fx1a/backup: the decrypted text holds no internal mark", inside != null && !inside.contains(mark) && !inside.contains(marked))
    }
}
