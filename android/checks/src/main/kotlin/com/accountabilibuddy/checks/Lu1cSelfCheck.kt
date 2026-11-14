package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.LunchRemote
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.WatchSnapshot
import com.accountabilibuddy.shared.WatchSnapshotJson
import com.accountabilibuddy.shared.sync.LunchDay
import com.accountabilibuddy.shared.sync.LunchDiscardOutcome
import com.accountabilibuddy.shared.sync.LunchDiscardSetting
import com.accountabilibuddy.shared.sync.LunchLockChange
import com.accountabilibuddy.shared.sync.LunchOps
import com.accountabilibuddy.shared.sync.LunchPiece
import com.accountabilibuddy.shared.sync.LunchReceiving
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.serialization.json.JsonObject
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.ZoneOffset
import java.util.UUID

/**
 * LU1c (phone and watch, ADR 88): the lunch X asks Count it / Discard it / Keep going. The pure rules the phone's LunchStore and the watch's
 * messages use ([LunchOps], [WatchLunch]) are run here; the Android wiring (the store, the notification, the dialog, the watch page) is checked by
 * scanning its source, because this module has no Context.
 */
object Lu1cSelfCheck {
    private val noon: Instant = Instant.parse("2026-11-13T12:00:00Z")
    private val pc = UUID.fromString("aaaaaaaa-0000-0000-0000-00000000000a")

    private fun at(minutes: Int): Instant = noon.plusSeconds(minutes * 60L)

    private fun sameDay(time: Instant) = time.atZone(ZoneOffset.UTC).toLocalDate() == noon.atZone(ZoneOffset.UTC).toLocalDate()

    private fun minutes(lunch: SyncLunch) = Duration.between(lunch.start, lunch.end).toMinutes()

    fun run(report: CheckReport) {
        val day = LunchDay.begin(at(0), Duration.ofMinutes(60))

        // Count it, then Start the rest.
        val counted = LunchOps.count(day, at(20))
        report.check("lu1c: Count it ends the piece now and asks the lock to come back", counted != null && counted.lunch.end == at(20) && counted.lockChange == LunchLockChange.RELOCK, "$counted")
        val afterCount = counted!!.lunch
        report.check("lu1c: after Count it nothing runs, 20 min are used and 40 are left", !LunchDay.isRunning(afterCount, at(21)) && LunchOps.leftMinutes(afterCount, at(21), 60) == 40)
        report.check("lu1c: Start the rest is offered after Count it", LunchOps.canStartRest(afterCount, at(30), 60))
        val rest = LunchOps.startRest(afterCount, at(30), 60)
        report.check("lu1c: Start the rest runs for the 40 min left and lifts the lock for that piece", rest != null && minutes(rest.lunch) == 40L && rest.lockChange == LunchLockChange.LIFT, "$rest")
        report.check("lu1c: the earlier piece is kept as counted, the allowance is unchanged", rest!!.lunch.counted == listOf(LunchPiece(at(0), at(20))) && rest.lunch.allowance == 60, "${rest.lunch}")
        report.check("lu1c: Start the rest is not offered while a piece runs", !LunchOps.canStartRest(rest.lunch, at(31), 60) && LunchOps.startRest(rest.lunch, at(31), 60) == null)

        // The rest never exceeds the allowance, through every count and rest, and never past 120 min.
        var lunch = day
        var clock = 0
        var worst = Duration.ZERO
        for (step in 1..6) {
            val ended = LunchOps.count(lunch, at(clock + 9)) ?: break
            clock += 12
            val next = LunchOps.startRest(ended.lunch, at(clock), 60) ?: break
            lunch = next.lunch
            val total = LunchDay.countedTotal(lunch).plus(Duration.between(lunch.start, lunch.end))
            if (total > worst) worst = total
        }
        report.check("lu1c: pieces started again together never exceed the day's 60 min allowance", worst > Duration.ZERO && worst <= Duration.ofMinutes(60), "$worst")
        val big = LunchDay.begin(at(0), Duration.ofMinutes(120))
        val bigCounted = LunchOps.count(big, at(100))!!.lunch
        val bigRest = LunchOps.startRest(bigCounted, at(101), 60)
        report.check("lu1c: the day's total stays at 120 min or less", bigRest != null && minutes(bigRest.lunch) == 20L && LunchDay.countedTotal(bigRest.lunch).plus(Duration.ofMinutes(minutes(bigRest.lunch))) <= LunchDay.MAX_LENGTH)
        val spent = LunchOps.count(day, noon.plusSeconds(3570))!!.lunch
        report.check("lu1c: with under a minute left there is no Start the rest", !LunchOps.canStartRest(spent, at(61), 60) && LunchOps.startRest(spent, at(61), 60) == null && !LunchOps.canStartRest(day.copy(end = at(60)), at(61), 60))

        // Discard it, then no automatic start.
        val discarded = LunchOps.discard(day, at(5), allowedByMode = true)
        report.check("lu1c: Discard it takes the piece back and asks the lock to come back", discarded != null && discarded.lunch.discarded && discarded.lunch.end == discarded.lunch.start && discarded.lockChange == LunchLockChange.RELOCK, "$discarded")
        report.check("lu1c: a discarded lunch never starts again by itself", !LunchDay.autoStartPending(discarded!!.lunch))
        report.check("lu1c: after Discard it the whole allowance is left for Start the rest", LunchOps.canStartRest(discarded.lunch, at(6), 60) && LunchOps.leftMinutes(discarded.lunch, at(6), 60) == 60)

        // Discard hidden when the mode's setting is off.
        report.check("lu1c: Discard it does nothing when the mode's setting is off", LunchOps.discard(day, at(5), allowedByMode = false) == null)
        val timeText = { t: Instant -> if (t == noon) "12:00 PM" else "?" }
        val asked = LunchOps.question(day, at(20), 60, allowedByMode = true, time = timeText)
        val askedOff = LunchOps.question(day, at(20), 60, allowedByMode = false, time = timeText)
        report.check("lu1c: the question offers Discard it when the setting is on", asked != null && asked.canDiscard && asked.discardText != null, "$asked")
        report.check("lu1c: the question hides Discard it when the setting is off", askedOff != null && !askedOff.canDiscard && askedOff.discardText == null, "$askedOff")
        report.check("lu1c: the question reads the start in 12-hour time and what Count it keeps", asked?.usedText == "Started 12:00 PM · 20 of 60 min used" && asked.countText == "Count it: 20 min count, 40 min stay for later today.", "${asked?.usedText} / ${asked?.countText}")
        report.check("lu1c: no question when nothing runs", LunchOps.question(afterCount, at(21), 60, true, timeText) == null && LunchOps.question(null, at(21), 60, true, timeText) == null)

        // The setting: off at once, on waits and never during a session.
        val off = LunchDiscardSetting().change(false, at(0), Duration.ofMinutes(10), sessionRunning = false)
        report.check("lu1c: turning the setting off applies at once", !off.first.allowed && off.second == LunchDiscardOutcome.APPLIED)
        val wanted = off.first.change(true, at(1), Duration.ofMinutes(10), sessionRunning = false)
        report.check("lu1c: turning it on waits the looser-change delay", !wanted.first.allowed && wanted.second == LunchDiscardOutcome.WAITING && wanted.first.pendingFrom == at(11))
        report.check("lu1c: turning it on during a session is refused", off.first.change(true, at(1), Duration.ofMinutes(10), sessionRunning = true).second == LunchDiscardOutcome.REFUSED)
        report.check("lu1c: a waiting 'on' takes effect only after the wait and with no session", !wanted.first.applyIfReady(at(5), false).allowed && !wanted.first.applyIfReady(at(12), true).allowed && wanted.first.applyIfReady(at(12), false).allowed)

        // A Count it or Discard it received from another device updates this phone's lunch, so the lock's hold ends (not on lunch any more).
        val running = LunchDay.begin(at(0), Duration.ofMinutes(60))
        val receivedCount = LunchReceiving().take(pc, running.copy(end = at(25), editedAt = at(25)), running, at(26), ::sameDay)
        report.check("lu1c: a Count it from the PC ends the lunch here", receivedCount != null && !LunchDay.isRunning(receivedCount, at(26)) && LunchDay.used(receivedCount, at(26)) == Duration.ofMinutes(25), "$receivedCount")
        val receivedDiscard = LunchReceiving().take(pc, running.copy(end = running.start, discarded = true, editedAt = at(25)), running, at(26), ::sameDay)
        report.check("lu1c: a Discard it from the PC ends the lunch here and uses nothing", receivedDiscard != null && !LunchDay.isRunning(receivedDiscard, at(26)) && receivedDiscard.discarded && LunchDay.used(receivedDiscard, at(26)) == Duration.ZERO, "$receivedDiscard")

        watchChecks(report, day)
        sourceChecks(report)
    }

    private fun watchChecks(report: CheckReport, day: SyncLunch) {
        val running = WatchLunch(true, 60, day.start, day.end, discardAllowed = true, allowanceMinutes = 60)
        val notAllowed = running.copy(discardAllowed = false)
        report.check("lu1c: the watch offers Discard it while lunch runs and the phone's mode allows it", running.canDiscard(at(20)))
        report.check("lu1c: the watch hides Discard it when the phone's mode does not allow it", !notAllowed.canDiscard(at(20)))
        report.check("lu1c: the watch hides Discard it when no lunch runs", !running.canDiscard(at(70)))
        report.check("lu1c: the watch's Discard it changes nothing when it is not allowed", notAllowed.expected(LunchRemote.DISCARD, at(20)) == notAllowed && running.expected(LunchRemote.DISCARD, at(20)).end == running.start)
        val counted = running.expected(LunchRemote.COUNT, at(20))
        report.check("lu1c: the watch's Count it ends the lunch, 40 min stay for the rest", counted.end == at(20) && counted.canStartRest(at(21)) && counted.leftMinutes(at(21)) == 40, "$counted")
        val rest = counted.expected(LunchRemote.REST, at(30))
        report.check("lu1c: the watch's Start the rest runs for the minutes left, no more", rest.start == at(30) && rest.end == at(70) && rest.countedMinutes == 20, "$rest")
        report.check("lu1c: an older phone's lunch has no Start the rest on the watch", !running.copy(allowanceMinutes = null).expected(LunchRemote.COUNT, at(20)).canStartRest(at(21)))
        report.check("lu1c: the watch sends the new actions the phone knows", listOf(LunchRemote.COUNT, LunchRemote.DISCARD, LunchRemote.REST).all { LunchRemote.read(LunchRemote.write(it)) == it })

        // The snapshot carries the discard-allowed mark (and only the day's minutes besides).
        fun snapshot(lunch: WatchLunch) = WatchSnapshot(
            createdAt = noon, linked = true, pcName = null, date = LocalDate.of(2026, 11, 13), parts = emptyList(), sessions = emptyList(), drift = emptyList(),
            plannedMinutes = 0, week = emptyList(), running = null, lunch = lunch,
        )
        val written = WatchSnapshotJson.write(snapshot(running))
        val back = WatchSnapshotJson.read(written)?.lunch
        report.check("lu1c: the watch snapshot carries discard allowed and the allowance", back == running, "$back")
        val lunchJson = written["lunch"] as JsonObject
        report.check("lu1c: the snapshot's lunch holds only times, minutes and the allowed mark", lunchJson.keys == setOf("workday", "lengthMinutes", "start", "end", "discardAllowed", "allowanceMinutes"), "${lunchJson.keys}")
        val noMark = WatchSnapshotJson.read(WatchSnapshotJson.write(snapshot(notAllowed)))?.lunch
        report.check("lu1c: a snapshot with Discard off reads back as off", noMark != null && !noMark.discardAllowed)
        val olderLunch = WatchLunch(true, 60, day.start, day.end)
        val older = WatchSnapshotJson.write(snapshot(olderLunch))["lunch"] as JsonObject
        report.check("lu1c: an older phone's snapshot (no mark) reads as not allowed", !older.containsKey("discardAllowed") && WatchSnapshotJson.read(WatchSnapshotJson.write(snapshot(olderLunch)))?.lunch?.discardAllowed == false)
    }

    private fun source(path: String): String = File(path).takeIf { it.exists() }?.readText()?.replace("\r\n", "\n") ?: ""

    private fun sourceChecks(report: CheckReport) {
        val store = source("../app/src/main/java/com/accountabilibuddy/companion/lunch/LunchStore.kt")
        val body = { signature: String -> store.substringAfter(signature, "").substringBefore("\n\n") }
        report.check("lu1c: LunchStore is found", store.isNotEmpty())
        report.check("lu1c: Count it, Discard it and Start the rest go through the pure LunchOps rules and one answer path",
            body("fun count(").contains("LunchOps.count(") && body("fun discard(").contains("LunchOps.discard(") && body("fun startRest(").contains("LunchOps.startRest(") &&
                listOf("fun count(", "fun discard(", "fun startRest(").all { body(it).contains("answer(") })
        val answer = store.substringAfter("private fun answer(", "").substringBefore("\n    }\n")
        report.check("lu1c: every answer that changes the lock asks LunchLockLift to lift or re-lock now (outside the store's lock)",
            answer.contains("lockChange != LunchLockChange.NONE") && answer.contains("LunchLockLift.check(context)") && answer.indexOf("synchronized(this)") in 0 until answer.indexOf("LunchLockLift.check"), answer.take(200))
        report.check("lu1c: I'm back is Count it", body("fun endNow(").contains("count("))
        report.check("lu1c: an answer reads trusted time, never the phone's clock", answer.contains("TrustedTime.now()") && !Regex("""(Instant|System)\.(now|currentTimeMillis)\(""").containsMatchIn(answer))
        report.check("lu1c: the watch snapshot gets the mode's discard setting from the phone", body("fun forWatch(").contains("discardAllowed = discardAllowed"))
        val app = source("../app/src/main/java/com/accountabilibuddy/companion/CompanionApp.kt")
        report.check("lu1c: the app hands the watch the current mode's setting (read after a due 'on' settles)",
            app.contains("lunch.forWatch(") && app.contains("lunchDiscardAllowed()") && app.substringAfter("fun lunchDiscardAllowed(", "").substringBefore("\n    }").let { it.contains("settleDiscard") && it.contains("isCompanion") })
        val fromWatch = source("../app/src/main/java/com/accountabilibuddy/companion/watch/LunchFromWatch.kt")
        report.check("lu1c: the phone applies the watch's Discard it only against its own mode setting", fromWatch.contains("lunch.discard(now, discardAllowed)") && fromWatch.contains("LunchRemote.REST") && fromWatch.contains("canStartRest"))
        val notifier = source("../app/src/main/java/com/accountabilibuddy/companion/notify/TimelineNotifier.kt")
        report.check("lu1c: the pinned lunch notification has an End lunch action that opens the question (it asks, it does not answer)",
            notifier.contains("\"End lunch\"") && notifier.contains("EXTRA_ASK_END") && notifier.contains("isOnLunch(") && !notifier.contains("lunch.discard(") && !notifier.contains("lunch.count("))
        val main = source("../app/src/main/java/com/accountabilibuddy/companion/MainActivity.kt")
        report.check("lu1c: the app opens the question from that action", main.contains("EXTRA_ASK_END") && main.contains("lunchAsk.value = true"))
        val dialog = source("../app/src/main/java/com/accountabilibuddy/companion/ui/LunchEndDialog.kt")
        report.check("lu1c: the dialog shows Discard it only from the question's discard text and re-reads the mode setting when answered",
            dialog.contains("question.discardText?.let") && dialog.contains("app.lunch.discard(TrustedTime.now(), app.lunchDiscardAllowed())") && dialog.contains("\"Count it\"") && dialog.contains("\"Keep going\""))
        val card = source("../app/src/main/java/com/accountabilibuddy/companion/ui/LunchCard.kt")
        report.check("lu1c: the Today lunch card has the Close icon, Start the rest and I'm back", card.contains("Icons.Outlined.Close") && card.contains("lunch.onAsk") && card.contains("Start the rest") && card.contains("lunch.onBack"))
        val section = source("../app/src/main/java/com/accountabilibuddy/companion/ui/LunchSection.kt")
        report.check("lu1c: the settings page has Allow discarding for both modes with the waiting state and a refusal in a session",
            section.contains("AccountabiliBuddy mode") && section.contains("ProductaBuddy mode") && section.contains("setDiscardAllowed(") && section.contains("lunchSessionRunning()") && section.contains("Cancel this change") && section.contains("LunchDiscardOutcome.REFUSED"))
        val page = source("../wear/src/main/java/com/accountabilibuddy/watch/ui/LunchPage.kt")
        report.check("lu1c: the watch page shows Discard it only through WatchLunch.canDiscard", page.contains("if (lunch.canDiscard(at))") && page.contains("LunchRemote.DISCARD") && page.contains("Keep going") && page.contains("LunchRemote.COUNT"))
        val link = source("../wear/src/main/java/com/accountabilibuddy/watch/link/LunchLink.kt")
        report.check("lu1c: the watch's at-once view of an answer is the shared WatchLunch.expected", link.contains("lunch.expected(action"))
        report.check("lu1c: LunchLockLift (essentials) is called from the store, not edited", store.contains("import com.accountabilibuddy.companion.essentials.LunchLockLift"))
    }
}
