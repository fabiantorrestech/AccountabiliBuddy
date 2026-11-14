package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.LunchDay
import com.accountabilibuddy.shared.sync.LunchDelivery
import com.accountabilibuddy.shared.sync.LunchDiscardSetting
import com.accountabilibuddy.shared.sync.LunchPeer
import com.accountabilibuddy.shared.sync.LunchPiece
import com.accountabilibuddy.shared.sync.LunchReceiving
import com.accountabilibuddy.shared.sync.SyncFileJson
import com.accountabilibuddy.shared.sync.SyncLunch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.ZoneOffset
import java.util.UUID

/**
 * LU1a (phone only parts; the day's rule, receive, equality, delivery, setting and wire tables are the shared lu1a fixtures): an older file reads as
 * unknown pieces (missing means keep), an empty list as none, the older reader's three fields are unchanged, a lunch received from an older app keeps
 * the counted pieces through LunchReceiving, and the delivery line says "On all your devices" for the same pieces.
 */
object Lu1aSelfCheck {
    private val noon: Instant = Instant.parse("2026-11-13T12:00:00Z")
    private val older = UUID.fromString("aaaaaaaa-0000-0000-0000-00000000000a")
    private val phone = UUID.fromString("bbbbbbbb-0000-0000-0000-00000000000b")

    private fun at(minutes: Int): Instant = noon.plusSeconds(minutes * 60L)

    private fun sameDay(time: Instant) = time.atZone(ZoneOffset.UTC).toLocalDate() == noon.atZone(ZoneOffset.UTC).toLocalDate()

    fun run(report: CheckReport) {
        report.check("lu1a: the day's lunch is never over 120 minutes", LunchDay.MAX_LENGTH == Duration.ofMinutes(120))

        val legacy = SyncFileJson.readLunch(Json.parseToJsonElement("""{"start":"2026-11-13T12:00:00+00:00","end":"2026-11-13T13:00:00+00:00","editedAt":"2026-11-13T12:00:00+00:00"}""").jsonObject)
        report.check("lu1a: a file without the new fields reads pieces and allowance as unknown", legacy.counted == null && legacy.allowance == null && !legacy.discarded, "$legacy")
        val none = SyncFileJson.readLunch(Json.parseToJsonElement("""{"start":"2026-11-13T12:00:00+00:00","end":"2026-11-13T13:00:00+00:00","counted":[]}""").jsonObject)
        report.check("lu1a: counted [] reads as none, not unknown", none.counted != null && none.counted!!.isEmpty(), "$none")
        val written = SyncFileJson.writeLunch(SyncLunch(at(0), at(60), at(0)))
        report.check("lu1a: a lunch with nothing new writes only start, end and stamp (an older reader sees what it always did)", written.keys == setOf("start", "end", "editedAt"), "${written.keys}")

        // An older app rewrites our piece 2 from its three fields: the received record keeps our counted pieces and allowance.
        val ours = SyncLunch(at(60), at(100), at(60), 60, listOf(LunchPiece(at(0), at(20))))
        val receiving = LunchReceiving()
        val fromOlder = SyncLunch(at(60), at(105), at(70))
        val taken = receiving.take(older, fromOlder, ours, at(72), ::sameDay)
        report.check("lu1a: an older app's +5 on piece 2 keeps the counted pieces and the allowance grows to cover it",
            taken?.counted == ours.counted && taken?.allowance == 65 && taken.end == at(105), "$taken")
        val again = receiving.take(older, fromOlder, taken, at(80), ::sameDay)
        report.check("lu1a: the same older file received again changes nothing", again == null, "$again")
        val stale = LunchReceiving().take(phone, SyncLunch(at(0), at(25), at(75)), ours, at(76), ::sameDay)
        report.check("lu1a: an older app's edit of a piece we already counted changes nothing", stale == null, "$stale")

        // The delivery line: the same pieces are on all devices, an older app on the same current piece too.
        val peers = listOf(LunchPeer("Phone", at(61), ours), LunchPeer("Old phone", at(61), SyncLunch(at(60), at(100), at(61))))
        report.check("lu1a: the delivery line says it is on all devices when the pieces match", LunchDelivery.line(ours, peers, at(62)) { "t" } == "On all your devices")
        val behind = listOf(LunchPeer("Phone", at(61), ours.copy(counted = emptyList())))
        report.check("lu1a: other counted pieces are not on that device yet", LunchDelivery.line(ours, behind, at(62)) { "12:01 PM" }?.startsWith("Not on Phone yet") == true)

        // S4: the keep path is idempotent for a fresh receiver too (the app restarted and the older file is read again), and a fresh receiver
        // gets the same result as the one that had read it before.
        val freshAgain = LunchReceiving().take(older, fromOlder, taken, at(80), ::sameDay)
        report.check("lu1a: the same older file read by a fresh receiver (app restart) changes nothing", freshAgain == null, "$freshAgain")
        val freshFirst = LunchReceiving().take(older, fromOlder, ours, at(72), ::sameDay)
        report.check("lu1a: a fresh receiver takes the same lunch from the older file as the first one did", freshFirst == taken, "$freshFirst against $taken")

        // B1: an older app's +5 on a lunch we discarded makes a running lunch, never a "discarded" one that runs.
        val discarded = SyncLunch(at(0), at(0), at(5), 60, emptyList(), true)
        val plusFive = SyncLunch(at(0), at(65), at(10))
        val afterPlusFive = LunchReceiving().take(older, plusFive, discarded, at(15), ::sameDay)
        report.check("lu1a: an older app's +5 on a discarded lunch is a running lunch whose minutes count",
            afterPlusFive != null && !afterPlusFive.discarded && afterPlusFive.end == at(65) && afterPlusFive.allowance == 65, "$afterPlusFive")
        report.check("lu1a: no received lunch is ever discarded and running at once",
            listOf(afterPlusFive, LunchDay.held(SyncLunch(at(0), at(60), at(5), 60, emptyList(), true))).filterNotNull().none { it.discarded && it.end.isAfter(it.start) })
        report.check("lu1a: that older +5 read again by a fresh receiver changes nothing", LunchReceiving().take(older, plusFive, afterPlusFive, at(20), ::sameDay) == null)

        // S1: new phone, PC on piece 2 and an older phone whose +5 on piece 2 is read before the PC's file, or after it: the same lunch either way.
        val piece1 = SyncLunch(at(0), at(20), at(20), 60, emptyList())
        val pcFile = SyncLunch(at(60), at(100), at(60), 60, listOf(LunchPiece(at(0), at(20))))
        val olderPlus = SyncLunch(at(60), at(105), at(70))
        val receiverA = LunchReceiving()
        val firstA = receiverA.take(older, olderPlus, piece1, at(72), ::sameDay)
        val finalA = receiverA.take(phone, pcFile, firstA, at(74), ::sameDay) ?: firstA
        val receiverB = LunchReceiving()
        val firstB = receiverB.take(phone, pcFile, piece1, at(61), ::sameDay)
        val finalB = receiverB.take(older, olderPlus, firstB, at(72), ::sameDay) ?: firstB
        val wanted = SyncLunch(at(60), at(105), at(70), 65, listOf(LunchPiece(at(0), at(20))))
        report.check("lu1a: the older +5 before the PC's file keeps the counted piece (3 devices)", finalA == wanted, "$finalA")
        report.check("lu1a: the PC's file before the older +5 ends on the same lunch", finalB == wanted, "$finalB")

        // S2: a restore never takes a waiting discard change, and an "on" in a file waits like any change (here: it is left as this phone has it).
        report.check("lu1a: a phone restore leaves a waiting discard change out",
            !LunchDiscardSetting.restoreTakesKey(LunchDiscardSetting.prefKey(false, "pending"), true) && !LunchDiscardSetting.restoreTakesKey(LunchDiscardSetting.prefKey(true, "pendingFrom"), null))
        report.check("lu1a: a phone restore never turns discarding on at once, only off",
            !LunchDiscardSetting.restoreTakesKey(LunchDiscardSetting.prefKey(true, "allowed"), true) && LunchDiscardSetting.restoreTakesKey(LunchDiscardSetting.prefKey(true, "allowed"), false))
        report.check("lu1a: a phone restore takes the rest of the lunch settings as before", LunchDiscardSetting.restoreTakesKey("lengthMinutes", null))
        report.check("lu1a: restoring off drops this phone's own waiting on for that mode",
            LunchDiscardSetting.restoreClears(LunchDiscardSetting.prefKey(true, "allowed"), false) == listOf("discard.companion.pending", "discard.companion.pendingFrom")
                && LunchDiscardSetting.restoreClears(LunchDiscardSetting.prefKey(true, "allowed"), true).isEmpty())

        report.check("lu1a: the discard setting is on by default, nothing waiting", LunchDiscardSetting() == LunchDiscardSetting(true, null, null))

        // Fix round 2: lunch runs on trusted time everywhere. LunchStore needs a Context, so this module can't run it; its source is scanned instead:
        // every read that decides "is lunch running" reads TrustedTime itself (whatever time a caller passes is ignored), no lunch read or the
        // over alarm uses the wall clock, and the alarm counts on the elapsed-realtime clock.
        val store = File("../app/src/main/java/com/accountabilibuddy/companion/lunch/LunchStore.kt")
        val source = if (store.exists()) store.readText().replace("\r\n", "\n") else ""
        val body = { signature: String -> source.substringAfter(signature, "").substringBefore("\n\n") }
        val wallClock = Regex("""(Instant|System)\.(now|currentTimeMillis)\(|RTC_WAKEUP""")
        val code = source.lines().filterNot { it.trimStart().startsWith("*") || it.trimStart().startsWith("//") || it.trimStart().startsWith("/*") }.joinToString("\n")
        report.check("lu1a: LunchStore is found", source.isNotEmpty())
        report.check("lu1a: isOnLunch, today, canStart, canShorten, overDue and forWatch read TrustedTime.now() themselves",
            listOf("fun isOnLunch(", "fun today(", "fun canStart(", "fun canShorten(", "fun overDue(", "fun forWatch(").all { body(it).contains("TrustedTime.now()") }, "a lunch read is missing TrustedTime.now()")
        report.check("lu1a: start, adjust, endNow and receive take their time from TrustedTime too",
            listOf("fun start(", "fun adjust(", "fun endNow(", "fun receive(").all { body(it).contains("val now = TrustedTime.now()") })
        report.check("lu1a: LunchStore never reads the phone's own clock (and the over alarm is not an RTC alarm)", !wallClock.containsMatchIn(code), wallClock.find(code)?.value ?: "")
        report.check("lu1a: the over alarm counts on the elapsed-realtime clock from trusted time", source.contains("ELAPSED_REALTIME_WAKEUP") && source.contains("Duration.between(TrustedTime.now(), lunch.end)"))
    }
}
