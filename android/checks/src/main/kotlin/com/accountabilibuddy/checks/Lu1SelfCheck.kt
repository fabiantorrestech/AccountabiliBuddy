package com.accountabilibuddy.checks

import com.accountabilibuddy.shared.sync.LunchListening
import com.accountabilibuddy.shared.sync.LunchWindow
import com.accountabilibuddy.shared.sync.SyncLunch
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneOffset

/**
 * LU1-fix: the "listen fast" rule (the PC's LunchListening is the same table): busy while a lunch runs here or in a received file, and in the
 * hour before a planned lunch window until its end when no lunch was taken; idle otherwise. Plus the future-stamp clamp.
 */
object Lu1SelfCheck {
    private val day: LocalDateTime = LocalDateTime.of(2026, 11, 13, 0, 0)
    private val window = LunchWindow(day.plusHours(12), day.plusHours(14))

    private fun instant(local: LocalDateTime): Instant = local.toInstant(ZoneOffset.UTC)

    private fun lunch(startHour: Long, minutes: Long, edited: Instant? = null) =
        SyncLunch(instant(day.plusHours(startHour)), instant(day.plusHours(startHour)).plus(Duration.ofMinutes(minutes)), edited)

    private fun busy(local: LocalDateTime, own: SyncLunch? = null, received: List<SyncLunch?> = emptyList(), planned: List<LunchWindow> = listOf(window)) =
        LunchListening.isListening(instant(local), local, own, received, planned)

    fun run(report: CheckReport) {
        report.check("lu1: idle long before the lead", !busy(day.plusHours(10)))
        report.check("lu1: idle just before the lead", !busy(day.plusHours(11).minusMinutes(1)))
        report.check("lu1: busy at the lead", busy(day.plusHours(11)))
        report.check("lu1: busy inside the window", busy(day.plusHours(13)))
        report.check("lu1: idle at the window's end", !busy(day.plusHours(14)))
        report.check("lu1: idle with no planned lunch", !busy(day.plusHours(13), planned = emptyList()))
        report.check("lu1: lead 0 would miss the hour before", LunchListening.LEAD == Duration.ofHours(1))

        val taken = lunch(12, 30)
        report.check("lu1: a lunch taken and over is not expected again", !busy(day.plusHours(13), own = taken))
        report.check("lu1: own lunch running is busy", busy(day.plusHours(12).plusMinutes(10), own = taken))
        report.check("lu1: own lunch running outside any window is busy", busy(day.plusHours(9).plusMinutes(10), own = lunch(9, 60), planned = emptyList()))

        val remote = lunch(9, 60)
        report.check("lu1: a lunch running in a received file is busy, no plan", busy(day.plusHours(9).plusMinutes(5), received = listOf(null, remote), planned = emptyList()))
        report.check("lu1: a received lunch that is over is not busy", !busy(day.plusHours(10).plusMinutes(5), received = listOf(remote), planned = emptyList()))
        report.check("lu1: a received lunch not started yet is not busy", !busy(day.plusHours(8), received = listOf(remote), planned = emptyList()))
        report.check("lu1: a lunch a minute ahead (a clock a little ahead) counts as running", busy(day.plusHours(8).plusMinutes(59), received = listOf(remote), planned = emptyList()))

        val now = instant(day.plusHours(12))
        val stamped = SyncLunch(now, now.plus(Duration.ofMinutes(60)), now.plus(Duration.ofHours(5)))
        report.check("lu1: clamp holds a far-future edit stamp to now", LunchListening.clampFuture(stamped, now).editedAt == now)
        val fine = SyncLunch(now, now.plus(Duration.ofMinutes(60)), now.plus(Duration.ofMinutes(1)))
        report.check("lu1: clamp leaves a stamp within tolerance", LunchListening.clampFuture(fine, now) == fine)
        val noStamp = SyncLunch(now.plus(Duration.ofHours(3)), now.plus(Duration.ofHours(4)))
        report.check("lu1: clamp stamps a far-future start with none", LunchListening.clampFuture(noStamp, now).editedAt == now)
        val past = SyncLunch(now.minusSeconds(600), now.plusSeconds(600), now.minusSeconds(300))
        report.check("lu1: clamp leaves a past lunch", LunchListening.clampFuture(past, now) == past)
        val newerReal = SyncLunch(now, now.plus(Duration.ofMinutes(65)), now.plusSeconds(30))
        report.check("lu1: a clamped stamp loses to a later real edit", LunchListening.clampFuture(stamped, now).lastEdit < newerReal.lastEdit)
    }
}
