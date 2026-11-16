package com.accountabilibuddy.checks

import com.accountabilibuddy.companion.session.PhoneSessionFiles
import com.accountabilibuddy.companion.stats.FocusWindows
import com.accountabilibuddy.companion.sync.ReceivedDevice
import com.accountabilibuddy.shared.session.PhoneSessionRecord
import com.accountabilibuddy.shared.session.PhoneSessionSyncInput
import com.accountabilibuddy.shared.session.PhoneSessionSyncWriter
import com.accountabilibuddy.shared.sync.ReceivedSyncFile
import com.accountabilibuddy.shared.sync.SyncFile
import com.accountabilibuddy.shared.sync.SyncFileJson
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.time.ZoneOffset
import java.util.UUID

/**
 * PS2b: the PS1a fixtures run through what this PHONE WRITES (the writer that makes the sync file's session part) and through how it COUNTS its own
 * sessions with the other devices'. Same fixture files as [Ps1Check] (`ps1/pieces`, `ps1/overlap`), another comparison: the piece cutting is
 * [Ps1Check]'s; here the whole path from the controller's snapshot to the file's JSON is compared with the PC's own output.
 */
class Ps2bCheck(override val name: String, private val compare: (FixtureCase) -> String?) : FixtureCheck {
    override val area = "ps1"

    override fun run(fixture: FixtureFile, report: CheckReport) {
        for (case in fixture.cases) {
            GoalCaseRunner.run(report, "ps2b/$name: ${case.name}") { compare(case) }
        }
    }
}

object Ps2bCases {
    private val deviceA: UUID = UUID.fromString("33333333-0000-0000-0000-00000000000c")

    // ------------------------------------------------------------------------------------------------------ pieces: what the phone writes

    fun pieces(case: FixtureCase): String? {
        val now = Ps1Cases.time(case.text("now"))
        val zone = ZoneOffset.ofTotalSeconds(case.input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
        val oldest = (case.input["oldestSessionStart"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { Ps1Cases.time(it.content) }
        val records = case.input.getValue("records").jsonArray.mapIndexed { index, item -> Ps1Cases.record(index, item.jsonObject) }
        val running = records.firstOrNull { it.endedAt == null }
        // "The oldest record the phone still keeps, of any kind" is a test session here: it makes the file speak from the 14-day edge and is never written.
        val older = oldest?.let { PhoneSessionRecord("oldest", "p", "Deep work", it, it, null, isTest = true, endedAt = it.plusSeconds(60)) }
        val log = records.filter { it.endedAt != null } + listOfNotNull(older)
        val input = PhoneSessionSyncInput(running, log, false, null, false, emptySet(), now, true, zone)

        val part = PhoneSessionSyncWriter.forFile(input, now)
        val file = SyncFile(1, Ps1Cases.card(part.writtenAt), part.sessions, emptyList(), null, emptyList(), part.coversFrom, running = part.running)
        val written = SyncFileJson.write(file)
        val expected = case.expected.jsonObject
        Ps1Cases.differs("sessions", expected.getValue("sessions"), written.getValue("sessions"))?.let { return it }
        Ps1Cases.differs("coversFrom", expected.getValue("coversFrom"), written.getValue("coversFrom"))?.let { return it }
        if (part.writtenAt != now) return "the file is stamped ${part.writtenAt}, not the session clock's $now (B4)"
        if (part.coversFrom.isAfter(part.writtenAt)) return "coversFrom ${part.coversFrom} is after the time written"

        val expectedStart = (expected["runningSessionStartedAt"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { Ps1Cases.time(it.content) }
        val runningJson = written["running"] as? kotlinx.serialization.json.JsonObject
        if ((expectedStart != null) != (runningJson != null)) return "running: C# ${if (expectedStart != null) "has" else "has no"} running, the phone wrote ${if (runningJson != null) "one" else "none"}"
        if (expectedStart != null) {
            val got = (runningJson!!["sessionStartedAt"] as? JsonPrimitive)?.takeIf { it !is JsonNull }?.let { Ps1Cases.time(it.content) }
            if (got != expectedStart) return "running.sessionStartedAt: C# $expectedStart vs the phone's $got"
        }

        // The phone's own counting reads the same pieces (every session it holds, from the same cutting).
        if (oldest == null) {
            val own = PhoneSessionSyncWriter.forCounting(input)
            val gotOwn = own?.let { SyncFileJson.write(SyncFile(1, Ps1Cases.card(it.writtenAt), it.sessions, emptyList(), null, emptyList())).getValue("sessions") }
            val wantOwn = expected.getValue("sessions")
            if (wantOwn.jsonArray.isEmpty()) {
                if (own != null && own.sessions.isNotEmpty()) return "counting: sessions where the PC wrote none"
            } else {
                Ps1Cases.differs("own sessions", wantOwn, gotOwn ?: return "counting: no sessions")?.let { return it }
            }
        }

        return null
    }

    // ------------------------------------------------------------------------------------------------------ overlap: own phone sessions counted with the others

    /** The case's own sessions become this phone's phone sessions; counted through [PhoneSessionFiles.focusFiles] with the PC's file they must give the PC's union. */
    fun overlap(case: FixtureCase): String? {
        val input = case.input
        val own = input["own"]?.jsonArray.orEmpty().map { it.jsonObject }
        // A phone session is never "added by hand": a case with one of those is the PC's History rule, not this phone's.
        if (own.any { it.getValue("added").jsonPrimitive.boolean }) return null
        val expected = case.expected.jsonObject
        val days = expected["days"]?.jsonArray ?: return null
        val now = Ps1Cases.time(case.text("now"))
        val zone = ZoneOffset.ofTotalSeconds(input.getValue("zoneOffsetMinutes").jsonPrimitive.int * 60)
        val records = own.mapIndexed { index, session ->
            val start = Ps1Cases.time(session.getValue("start").jsonPrimitive.content)
            PhoneSessionRecord("own$index", "p", "Deep work", start, start, null, endedAt = Ps1Cases.time(session.getValue("end").jsonPrimitive.content))
        }
        val others = Ps1Cases.received(case, now).map { ReceivedDevice(it.file, it.receivedAt, "folder", ByteArray(0)) }
        val ownId = UUID(0, 99)
        val device = PhoneSessionFiles.ownDevice(PhoneSessionSyncInput(null, records, false, null, false, emptySet(), now, true, zone), ownId, "Pixel", "0.1.78")
        val files = PhoneSessionFiles.focusFiles(others, device).map { ReceivedSyncFile(it.file, it.receivedAt) }
        val windows = FocusWindows.of(files, zone, now)
        val dates = days.map { java.time.LocalDate.parse(it.jsonObject.getValue("date").jsonPrimitive.content) }
        val counted = FocusWindows.daysIn(windows, dates.first(), dates.last())
        for ((index, day) in days.withIndex()) {
            val want = day.jsonObject.getValue("focusedSeconds").jsonPrimitive.content.toDouble().toLong()
            val got = counted[index].focused.seconds
            if (want != got) return "${dates[index]}: focused seconds C# $want vs kotlin (own phone sessions through focusFiles) $got"
        }

        return null
    }
}
