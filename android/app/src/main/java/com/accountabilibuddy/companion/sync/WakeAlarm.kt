package com.accountabilibuddy.companion.sync

import com.accountabilibuddy.companion.model.LocalWindow
import java.time.Duration
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime

/** An alarm Android said was next ([at]), which app set it ([app]; null when Android didn't say), and when it was seen. */
data class AlarmSighting(val at: Instant, val app: String?, val seenAt: Instant)

/**
 * Which phone alarm is the wake-up alarm (ADR 23): Android only tells apps the soonest alarm clock of any app, and
 * calendar apps set alarm clocks for reminders. So the phone keeps what it saw, learns the app whose alarms go off in
 * the morning window of the sleep plan on two or more mornings, lets the user pin one, and keeps the wake-up app's
 * last alarm while another app's alarm is the one Android shows.
 */
object WakeAlarm {
    // Eight mornings of each kind (ADR 40's wake estimate) fit beside calendar reminders.
    private const val KEPT = 60
    private const val MORNINGS_TO_LEARN = 2

    // After the plan's wake time, an alarm still counts as the morning's this long (snoozes, late starts).
    private val AFTER_WAKE: Duration = Duration.ofHours(3)

    // Before this hour an alarm belongs to the night before (6:15 → last night); from it, to tonight.
    private const val EVENING_FROM_HOUR = 18

    /** Calendar apps set alarm clocks for reminders; they're never the wake-up alarm. */
    fun isCalendar(app: String?): Boolean = app?.contains("calendar", ignoreCase = true) == true

    /** [at] falls between its night's bedtime and the plan's wake time + 3 h ([night]: the plan's night of an evening). */
    fun inMorningWindow(at: LocalDateTime, night: (LocalDate) -> LocalWindow): Boolean {
        val evening = if (at.hour < EVENING_FROM_HOUR) at.toLocalDate().minusDays(1) else at.toLocalDate()
        val window = night(evening)
        return !at.isBefore(window.start) && !at.isAfter(window.end.plus(AFTER_WAKE))
    }

    /** [history] with [sighting] (the same app and time once, the newest sighting kept), oldest first, at most 60. */
    fun remember(history: List<AlarmSighting>, sighting: AlarmSighting): List<AlarmSighting> =
        (history.filterNot { it.app == sighting.app && it.at == sighting.at } + sighting).takeLast(KEPT)

    /** The app whose alarms fell in the morning window on at least two different mornings (most mornings wins), or null. */
    fun learned(history: List<AlarmSighting>, inWindow: (Instant) -> Boolean): String? =
        history
            .filter { it.app != null && !isCalendar(it.app) && inWindow(it.at) }
            .groupBy { it.app!! }
            .mapValues { (_, sightings) -> sightings.map { it.at.epochSecond / 86_400 }.distinct().size }
            .filterValues { it >= MORNINGS_TO_LEARN }
            .maxByOrNull { it.value }
            ?.key

    /**
     * The wake-up alarm now: [pinned] (or else [learned])'s alarm, the one Android shows [next] or, while another app's
     * is shown, the last one seen from it that's still ahead; when it has none ahead, or with no app pinned or learned yet,
     * the next alarm unless a calendar app set it.
     */
    fun choose(next: AlarmSighting?, history: List<AlarmSighting>, pinned: String?, learned: String?, now: Instant): Instant? {
        val app = pinned ?: learned
        if (app == null) {
            return next?.takeIf { !isCalendar(it.app) && it.at.isAfter(now) }?.at
        }

        if (next != null && next.app == app && next.at.isAfter(now)) {
            return next.at
        }

        return history.filter { it.app == app && it.at.isAfter(now) }.maxByOrNull { it.seenAt }?.at
            // The wake-up app has nothing ahead (e.g. a one-off alarm set in Clock): the next alarm, unless a calendar's.
            ?: next?.takeIf { !isCalendar(it.app) && it.at.isAfter(now) }?.at
    }
}
