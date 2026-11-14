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

        report.check("lu1a: the discard setting is on by default, nothing waiting", LunchDiscardSetting() == LunchDiscardSetting(true, null, null))
    }
}
