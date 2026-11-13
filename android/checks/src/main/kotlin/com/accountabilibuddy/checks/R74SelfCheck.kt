package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.goals.GoalChangeOutcome
import com.accountabilibuddy.shared.goals.GoalDeepWorkDaily
import com.accountabilibuddy.shared.goals.GoalDeepWorkDailyRules
import com.accountabilibuddy.shared.goals.GoalDeepWorkWeeklyResult
import com.accountabilibuddy.shared.goals.GoalMenuItemId
import com.accountabilibuddy.shared.goals.GoalMenuPlan
import com.accountabilibuddy.shared.goals.GoalCounting
import com.accountabilibuddy.shared.goals.GoalSourceData
import com.accountabilibuddy.shared.goals.GoalDefaults
import com.accountabilibuddy.shared.sync.FieldStamp
import com.accountabilibuddy.shared.tasks.SyncTaskAccountSettings
import com.accountabilibuddy.shared.tasks.SyncTaskItem
import com.accountabilibuddy.shared.tasks.SyncTaskLabel
import com.accountabilibuddy.shared.tasks.TaskAccountSettingsEdits
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskFieldStamps
import com.accountabilibuddy.shared.tasks.TaskHideShelf
import com.accountabilibuddy.shared.tasks.TaskHideShelfResult
import com.accountabilibuddy.shared.tasks.TaskHideShelfRule
import com.accountabilibuddy.shared.tasks.TaskItem
import com.accountabilibuddy.shared.tasks.TaskLabel
import com.accountabilibuddy.shared.tasks.TaskLabelColors
import com.accountabilibuddy.shared.tasks.TaskLabelEdits
import com.accountabilibuddy.shared.tasks.TaskLabelNameCheck
import com.accountabilibuddy.shared.tasks.TaskLabelWords
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskNumbering
import com.accountabilibuddy.shared.tasks.TaskReminder
import com.accountabilibuddy.shared.tasks.TaskReminderEdits
import com.accountabilibuddy.shared.tasks.TaskStoreRebuild
import java.time.Duration
import java.time.LocalDate
import java.time.LocalTime
import java.time.OffsetDateTime

/**
 * R7.4 + NG1 (phone only: the PC's twins of these rules are in its check programs and fixtures for them are exported by the PC lane, so
 * these run the same cases by hand): the account-settings edits (TaskAccountSettingsEdits), label colours and ring edits, reminder/due/
 * device edits, the Hide the Shelf looser-change rule, Rebuild's set-aside rule, and Deep work's phone-owned daily target (NG1).
 */
object R74SelfCheck {
    private const val SELF = "bbbbbbbb-0000-0000-0000-00000000000b"
    private const val OTHER = "cccccccc-0000-0000-0000-00000000000c"
    private const val PC = "aaaaaaaa-0000-0000-0000-00000000000a"
    private val now: OffsetDateTime = OffsetDateTime.parse("2026-11-13T17:00:00Z")
    private val delay: Duration = Duration.ofMinutes(10)

    fun run(report: CheckReport) {
        accountEdits(report)
        colours(report)
        labels(report)
        reminders(report)
        hideShelf(report)
        rebuild(report)
        dailyTarget(report)
        menuAndNumbering(report)
    }

    private fun accountEdits(report: CheckReport) {
        val n = "r74/account"
        val first = TaskAccountSettingsEdits.withDefaultView(null, "view-1", now, SELF)
        report.check("$n: a default view is chosen and stamped now by this device", first.defaultViewId == "view-1" && first.defaultViewStamp == FieldStamp(now, SELF))
        report.check("$n: choosing the same view again changes nothing", TaskAccountSettingsEdits.withDefaultView(first, "VIEW-1", now.plusMinutes(5), SELF) == first)
        val ahead = first.copy(defaultViewStamp = FieldStamp(now.plusHours(1), OTHER))
        val over = TaskAccountSettingsEdits.withDefaultView(ahead, null, now, SELF)
        report.check("$n: an edit over a stamp that is ahead is stamped just after it", over.defaultViewId == null && over.defaultViewStamp!!.at.isAfter(now.plusHours(1)))
        val adopted = TaskAccountSettingsEdits.adopted(null, "legacy", " cal ", SELF)
        report.check("$n: adopted values carry the adopted stamp and are not a real choice", adopted.defaultViewId == "legacy" && adopted.pickedCalendarId == "cal" && !TaskAccountSettingsEdits.isChosen(adopted.defaultViewStamp))
        val choice = TaskAccountSettingsEdits.withDefaultView(adopted, "legacy", now, SELF)
        report.check("$n: choosing the adopted value is stamped now (it can't lose a tie)", TaskAccountSettingsEdits.isChosen(choice.defaultViewStamp))
        val kept = TaskAccountSettingsEdits.adopted(choice, "other", "x", SELF)
        report.check("$n: adopting never overwrites a field that was chosen or adopted", kept.defaultViewId == "legacy" && kept.pickedCalendarId == "cal")
        val ring = TaskAccountSettingsEdits.withRingOn(null, listOf(SELF.uppercase(), OTHER), now, SELF)
        report.check("$n: a ring list is stored normalized (lower case, sorted)", ring.ringOn == listOf(SELF, OTHER).sorted())
        report.check("$n: the same list again changes nothing", TaskAccountSettingsEdits.withRingOn(ring, listOf(OTHER, SELF), now.plusMinutes(1), SELF) == ring)
        report.check("$n: null (every device) is a choice too", TaskAccountSettingsEdits.withRingOn(ring, null, now.plusMinutes(1), SELF).let { it.ringOn == null && it.ringOnStamp != ring.ringOnStamp })
        report.check("$n: a blank calendar clears it", TaskAccountSettingsEdits.withPickedCalendar(null, "  ", now, SELF).pickedCalendarId == null)
        val phones = listOf(SELF, OTHER)
        val off = com.accountabilibuddy.shared.tasks.TaskRingDevices.toggled(null, phones, OTHER, false)
        report.check("$n: turning one phone off names the other; turning it on again is every device", off == listOf(SELF) && com.accountabilibuddy.shared.tasks.TaskRingDevices.toggled(off, phones, OTHER, true) == null)
    }

    private fun colours(report: CheckReport) {
        val n = "r74/colour"
        report.check("$n: typed hex without # is read and upper-cased", TaskLabelColors.typed("2f6bea") == "#2F6BEA")
        report.check("$n: typed #RRGGBBAA keeps its opacity as rgba", TaskLabelColors.typed("#2F6BEA80") == "rgba(47,107,234,0.5)")
        report.check("$n: an opacity under 20% is raised to 20%", TaskLabelColors.withOpacity("#2F6BEA", 0.05) == "rgba(47,107,234,0.2)")
        report.check("$n: full opacity is plain hex", TaskLabelColors.withOpacity("rgba(47,107,234,0.5)", 1.0) == "#2F6BEA")
        report.check("$n: not a colour is null", TaskLabelColors.typed("blue") == null && TaskLabelColors.typed("") == null)
        report.check("$n: two decimals at most", TaskLabelColors.withOpacity("#000000", 0.333) == "rgba(0,0,0,0.33)")
        report.check("$n: same colour ignoring opacity", TaskLabelColors.sameOpaque("#2f6bea", "rgba(47,107,234,0.4)"))
    }

    private fun task(n: Int, label: String?, done: Boolean = false): SyncTaskItem {
        val at = now.minusDays(2)
        val item = TaskItem("74000000-0000-0000-0000-%012d".format(n), "T$n", TaskBin.NEXT, n.toDouble(), at, at, at, labelId = label, doneAt = if (done) at else null)
        return SyncTaskItem(item, TaskFieldStamps.allAt(FieldStamp(at, PC)))
    }

    private fun labels(report: CheckReport) {
        val n = "r74/labels"
        val work = TaskLabel(TaskLabel.idFor("Work"), "Work", "#2F6BEA", 1.0, now.minusDays(3))
        val lists = TaskLists(listOf(task(1, work.id), task(2, work.id), task(3, work.id, done = true), task(4, null)), listOf(SyncTaskLabel(work)), emptyList(), emptyList())
        report.check("$n: use count is the open tasks only", TaskLabelEdits.useCount(lists, work.id) == 2)
        report.check("$n: a taken name (any case) is refused for a new label", TaskLabelEdits.checkName(lists, null, " work ") == TaskLabelNameCheck.Taken)
        report.check("$n: the label's own name is fine when renaming it", TaskLabelEdits.checkName(lists, work.id, "WORK") == TaskLabelNameCheck.Fine)
        report.check("$n: blank is refused", TaskLabelEdits.checkName(lists, null, "   ") == TaskLabelNameCheck.Blank)
        val ringed = TaskLabelEdits.setRingOn(lists, work.id, listOf(SELF), now, SELF)
        val label = ringed.labels.single().label
        report.check("$n: ring list set with its own stamp, the label edit time moves", label.ringOn == listOf(SELF) && label.ringOnStamp == FieldStamp(now, SELF) && label.editedAt == now)
        report.check("$n: the same ring list changes nothing", TaskLabelEdits.setRingOn(ringed, work.id, listOf(SELF), now.plusMinutes(1), SELF) == ringed)
        val again = TaskLabelEdits.setRingOn(ringed, work.id, null, now, SELF).labels.single().label
        report.check("$n: a second edit at the same instant is stamped after the first", again.ringOnStamp!!.at.isAfter(now) && again.ringOn == null)
        report.check("$n: unknown label changes nothing", TaskLabelEdits.setRingOn(lists, "nope", listOf(SELF), now, SELF) == lists)
        report.check("$n: words", TaskLabelWords.subtitle(3, null, 2) == "3 tasks · rings: all devices" && TaskLabelWords.subtitle(1, listOf(SELF), 2) == "1 task · rings: 1 of 2 phones" && TaskLabelWords.subtitle(0, emptyList(), 1) == "0 tasks · rings: PCs only")
    }

    private fun reminders(report: CheckReport) {
        val n = "r74/reminders"
        val base = task(1, null).task
        val a = TaskReminderEdits.add(base, "r1", TaskReminder.TEN_MINUTES_BEFORE, null)!!
        report.check("$n: a relative reminder is added", a.reminders!!.single().rule == TaskReminder.TEN_MINUTES_BEFORE)
        report.check("$n: the same relative rule twice is refused", TaskReminderEdits.add(a, "r2", TaskReminder.TEN_MINUTES_BEFORE, null) == null)
        report.check("$n: an unknown rule and an absolute one without a moment are refused", TaskReminderEdits.add(base, "r", "weird", null) == null && TaskReminderEdits.add(base, "r", TaskReminder.ABSOLUTE, null) == null)
        val abs = TaskReminderEdits.add(a, "r3", TaskReminder.ABSOLUTE, now)!!
        report.check("$n: an absolute reminder keeps its moment; the same moment twice is refused", abs.reminders!!.size == 2 && TaskReminderEdits.add(abs, "r4", TaskReminder.ABSOLUTE, now.withOffsetSameInstant(java.time.ZoneOffset.ofHours(2))) == null)
        var full = base
        listOf(TaskReminder.AT_START, TaskReminder.TEN_MINUTES_BEFORE, TaskReminder.ONE_HOUR_BEFORE, TaskReminder.DAY_BEFORE_AT_NINE).forEachIndexed { i, rule -> full = TaskReminderEdits.add(full, "f$i", rule, null)!! }
        full = TaskReminderEdits.add(full, "f9", TaskReminder.ABSOLUTE, now)!!
        report.check("$n: at most ${TaskReminder.MAX_PER_TASK} reminders", full.reminders!!.size == TaskReminder.MAX_PER_TASK && TaskReminderEdits.add(full, "f10", TaskReminder.ABSOLUTE, now.plusDays(1)) == null)
        report.check("$n: removing the last leaves none (null)", TaskReminderEdits.remove(a, "r1")!!.reminders == null && TaskReminderEdits.remove(a, "zz") == null)
        val due = TaskReminderEdits.setDue(base, LocalDate.of(2026, 11, 20), LocalTime.of(15, 0))
        report.check("$n: a due date with a time; removing the date removes the time", due.dueAt == LocalTime.of(15, 0) && TaskReminderEdits.setDue(due, null, LocalTime.of(9, 0)).let { it.dueOn == null && it.dueAt == null })
        report.check("$n: only this device / everywhere", TaskReminderEdits.setDevices(base, true, SELF.uppercase()).devices == listOf(SELF) && TaskReminderEdits.setDevices(base.copy(devices = listOf(SELF)), false, SELF).devices == null)
    }

    private fun hideShelf(report: CheckReport) {
        val n = "r74/hide-shelf"
        val hidden = TaskHideShelf(true)
        val (hid, r1) = TaskHideShelfRule.set(hidden, true, false, true, now, delay)
        report.check("$n: hiding when hidden is unchanged", hid == hidden && r1 == TaskHideShelfResult.Unchanged)
        val (queued, r2) = TaskHideShelfRule.set(hidden, false, false, false, now, delay)
        report.check("$n: showing the Shelf in AccountabiliBuddy mode waits the delay (looser)", r2 == TaskHideShelfResult.Waiting && queued.hide && queued.showAt == now.plus(delay))
        val (_, r3) = TaskHideShelfRule.set(hidden, false, false, true, now, delay)
        report.check("$n: refused during a session", r3 == TaskHideShelfResult.RefusedDuringSession)
        val (pb, r4) = TaskHideShelfRule.set(hidden, false, true, true, now, delay)
        report.check("$n: ProductaBuddy shows it at once, also in a session", r4 == TaskHideShelfResult.Applied && !pb.hide)
        val (again, _) = TaskHideShelfRule.set(queued, false, false, false, now.plusMinutes(5), delay)
        report.check("$n: a wait already running keeps its time", again.showAt == queued.showAt)
        val (dropped, r5) = TaskHideShelfRule.set(queued, true, false, true, now.plusMinutes(1), delay)
        report.check("$n: hiding (stricter) applies at once, also in a session, and drops the wait", r5 == TaskHideShelfResult.Applied && dropped == TaskHideShelf(true, null))
        report.check("$n: settle waits for the time", TaskHideShelfRule.settle(queued, false, false, now.plusMinutes(9)) == queued)
        report.check("$n: settle applies when due and no session", !TaskHideShelfRule.settle(queued, false, false, now.plusMinutes(10)).hide)
        report.check("$n: settle never applies during a session", TaskHideShelfRule.settle(queued, false, true, now.plusHours(2)) == queued)
    }

    private fun rebuild(report: CheckReport) {
        val n = "r74/rebuild"
        report.check("$n: an earlier set-aside copy is kept, never overwritten", TaskStoreRebuild.keptAside("stored", "old") == "old")
        report.check("$n: with none set aside the stored copy is kept", TaskStoreRebuild.keptAside("stored", null) == "stored")
        report.check("$n: nothing to keep is null", TaskStoreRebuild.keptAside(null, null) == null)
        report.check("$n: the copy is never dropped while one exists", listOf("a" to "b", "a" to null, null to "b").all { (s, o) -> TaskStoreRebuild.keptAside(s, o) != null })
        report.check("$n: refused when the list opens, and during a session", TaskStoreRebuild.refusal(false, false) != null && TaskStoreRebuild.refusal(true, true) != null && TaskStoreRebuild.refusal(true, false) == null)
    }

    private fun dailyTarget(report: CheckReport) {
        val n = "r74/ng1-daily"
        val none = GoalDeepWorkDaily()
        val ab = GoalDeepWorkDailyRules.set(none, 120, false, false, false, now, delay)
        report.check("$n: setting a first target applies at once", ab.state.minutes == 120 && ab.result == GoalDeepWorkWeeklyResult.Applied)
        val raise = GoalDeepWorkDailyRules.set(ab.state, 180, false, true, false, now, delay)
        report.check("$n: raising applies at once, also in a session", raise.state.minutes == 180)
        val lower = GoalDeepWorkDailyRules.set(ab.state, 60, false, false, false, now, delay)
        report.check("$n: lowering in AccountabiliBuddy waits the delay (not applied at once)", lower.result == GoalDeepWorkWeeklyResult.Waiting && lower.state.minutes == 120 && lower.state.pending?.appliesAt == now.plus(delay))
        report.check("$n: lowering is refused in a session", GoalDeepWorkDailyRules.set(ab.state, 60, false, true, false, now, delay).result == GoalDeepWorkWeeklyResult.RefusedDuringSession)
        val pb = GoalDeepWorkDailyRules.set(ab.state, 60, true, true, false, now, delay)
        report.check("$n: lowering in ProductaBuddy applies at once", pb.state.minutes == 60 && pb.result == GoalDeepWorkWeeklyResult.Applied)
        report.check("$n: removing the target waits too", GoalDeepWorkDailyRules.set(ab.state, null, false, false, false, now, delay).result == GoalDeepWorkWeeklyResult.Waiting)
        report.check("$n: clamped to 15 min to 8 h", GoalDeepWorkDailyRules.set(none, 5, false, false, false, now, delay).state.minutes == 15 && GoalDeepWorkDailyRules.set(none, 999, false, false, false, now, delay).state.minutes == 480)
        val settledEarly = GoalDeepWorkDailyRules.settle(lower.state, false, false, false, now.plusMinutes(9), delay)
        val settled = GoalDeepWorkDailyRules.settle(lower.state, false, false, false, now.plusMinutes(10), delay)
        report.check("$n: a waiting lowering applies at its time, not before, never in a session", settledEarly.minutes == 120 && settled.minutes == 60 && GoalDeepWorkDailyRules.settle(lower.state, false, true, false, now.plusHours(3), delay).minutes == 120)
        report.check("$n: cancel drops the wait", GoalDeepWorkDailyRules.cancel(lower.state).pending == null)
        val mine = GoalDeepWorkDaily(90)
        report.check("$n: no PC: the phone's own target counts", GoalDeepWorkDailyRules.effective(false, null, mine) == 90)
        report.check("$n: a PC in the group: the PC's wins always, the phone's own stays unused", GoalDeepWorkDailyRules.effective(true, 45, mine) == 45 && GoalDeepWorkDailyRules.effective(true, null, mine) == null)
        report.check("$n: the outcome enum is the shared one", GoalChangeOutcome.entries.isNotEmpty())
    }

    private fun menuAndNumbering(report: CheckReport) {
        val n = "r74/ng1-words"
        val deep = GoalDefaults.newDeepWork(GoalDefaults.WORK_AREA_ID, now)
        val day = LocalDate.of(2026, 11, 13)
        val state = GoalCounting.day(deep, GoalSourceData(emptyList(), emptyList(), java.time.ZoneId.of("UTC")), day, day)
        val with = GoalMenuPlan.of(deep, state, false, 0, null)
        val without = GoalMenuPlan.of(deep, state, false, 0, null, noPc = true)
        report.check("$n: with a PC the menu says the target is set on the PC", with.any { it.id == GoalMenuItemId.TrackTimeOnly && it.note!!.contains("PC") })
        report.check("$n: with no PC no menu line says 'on your PC'", without.none { (it.note ?: "").contains("PC") })
        report.check("$n: no group: the phone numbers its own tasks final", TaskNumbering.groupNumberingDevice(false, emptyList(), SELF) == SELF)
        report.check("$n: joined with no file yet: still waits (a PC may be there)", TaskNumbering.groupNumberingDevice(true, emptyList(), SELF) == null)
        report.check("$n: joined with a PC: the PC numbers", TaskNumbering.groupNumberingDevice(true, listOf(PC to "pc"), SELF) == PC)
        report.check("$n: the empty id before any group is not a numbering device", TaskNumbering.groupNumberingDevice(false, emptyList(), FieldStamp.NOBODY) == null)
        report.check("$n: account settings carry no text", SyncTaskAccountSettings.EMPTY.isEmpty)
    }
}
