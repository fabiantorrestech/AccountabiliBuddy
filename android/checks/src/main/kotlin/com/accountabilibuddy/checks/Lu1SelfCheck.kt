package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.LunchListening
import com.accountabilibuddy.shared.sync.LunchReceiving
import com.accountabilibuddy.shared.sync.SyncLog
import com.accountabilibuddy.shared.sync.SyncLogDirection
import com.accountabilibuddy.shared.sync.SyncLunch
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneOffset
import java.util.UUID

/**
 * LU1-fix (phone only parts; the listening and clamp tables are the shared lu1 fixtures): a lunch received again is taken once, so a clamped
 * stamp is applied once and a local edit in between stays; and the sync log's lunch lines (times only, one per sender and change).
 */
object Lu1SelfCheck {
    private val start: Instant = Instant.parse("2026-11-13T12:00:00Z")
    private val fastClock = UUID.fromString("cccccccc-0000-0000-0000-00000000000c")
    private val other = UUID.fromString("dddddddd-0000-0000-0000-00000000000d")

    private fun sameDay(time: Instant) = time.atZone(ZoneOffset.UTC).toLocalDate() == start.atZone(ZoneOffset.UTC).toLocalDate()

    fun run(report: CheckReport) {
        report.check("lu1: the lead is one hour", LunchListening.LEAD == Duration.ofHours(1))
        report.check("lu1: the longest lunch is 120 minutes", LunchListening.MAX_LENGTH == Duration.ofMinutes(120))

        // A phone clock 5 hours ahead stamps its lunch in the future; here it is held to now, a real edit after it wins, and the same file read
        // again later (the clamp would give a newer "now") changes nothing.
        val receiving = LunchReceiving()
        val fromFast = SyncLunch(start, start.plus(Duration.ofMinutes(60)), start.plus(Duration.ofHours(5)))
        val taken = receiving.take(fastClock, fromFast, null, start, ::sameDay)
        report.check("lu1: a future-stamped lunch is taken with its stamp held to now", taken?.editedAt == start, "$taken")
        val edited = taken!!.copy(end = taken.end.plus(Duration.ofMinutes(5)), editedAt = start.plusSeconds(60))
        val later = start.plusSeconds(600)
        val again = receiving.take(fastClock, fromFast, edited, later, ::sameDay)
        report.check("lu1: the same fast-clock file received again changes nothing, the +5 stays", again == null, "$again")
        val changed = receiving.take(fastClock, fromFast.copy(end = fromFast.end.plus(Duration.ofMinutes(10))), edited, later, ::sameDay)
        report.check("lu1: a changed record from the same sender is looked at again", changed?.editedAt == later, "$changed")
        val third = receiving.take(other, fromFast, edited, later, ::sameDay)
        report.check("lu1: another sender's identical record is looked at too (the record is kept per sender)", third?.editedAt == later, "$third")
        val notToday = receiving.take(fastClock, SyncLunch(start.minus(Duration.ofDays(2)), start.minus(Duration.ofDays(2)).plusSeconds(600)), null, later, ::sameDay)
        report.check("lu1: a lunch of another day is not taken", notToday == null)
        val long = LunchReceiving().take(other, SyncLunch(start, start.plus(Duration.ofHours(5)), start), null, start, ::sameDay)
        report.check("lu1: a received end past the longest lunch is held to it", long?.end == start.plus(Duration.ofMinutes(120)), "$long")
        val newerReal = SyncLunch(start, start.plus(Duration.ofMinutes(65)), start.plusSeconds(30))
        report.check("lu1: a clamped stamp loses to a later real edit", LunchListening.clampFuture(fromFast, start).lastEdit < newerReal.lastEdit)

        // The phone's sync log: a lunch line with times only and the edit stamp; the same lunch of the same sender once, two senders in turn once each.
        val file = File.createTempFile("lu1-synclog", ".log")
        try {
            val log = SyncLog(file) { OffsetDateTime.ofInstant(start, ZoneOffset.UTC) }
            val mine = SyncLunch(start, start.plus(Duration.ofMinutes(60)), start)
            val theirs = mine.copy(end = mine.end.plus(Duration.ofMinutes(10)), editedAt = null)
            repeat(4) {
                log.lunch("folder", SyncLogDirection.READ, "a", mine)
                log.lunch("folder", SyncLogDirection.READ, "b", theirs)
                log.lunch("relay", SyncLogDirection.SEND, "me", mine)
            }

            val lines = log.lines().filter { " lunch start=" in it }
            report.check("lu1: the phone logs one lunch line per sender and change, read and written", lines.size == 3, lines.joinToString(" | "))
            report.check(
                "lu1: a lunch line is times only, with the edit stamp (none when unstamped)",
                lines.any { it.endsWith("folder read lunch start=2026-11-13T12:00:00Z end=2026-11-13T13:00:00Z edited=2026-11-13T12:00:00Z") } &&
                    lines.any { it.endsWith("folder read lunch start=2026-11-13T12:00:00Z end=2026-11-13T13:10:00Z edited=none") } &&
                    lines.any { it.contains("relay send lunch") },
                lines.joinToString(" | "),
            )
        } finally {
            file.delete()
        }
    }
}
