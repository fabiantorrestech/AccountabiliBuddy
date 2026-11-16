package com.accountabilibuddy.shared.session

import java.time.Duration
import java.time.Instant

/**
 * What the phone's own use looked like inside a phone session (ADR 85, plan section 3 PS2 (3)): kept in the session log on this phone only, shown on the
 * phone's session card and Stats day panel, NEVER synced (R14). Null = could not be read (no Usage access, before Android 9): shown as a dash, never as zero.
 *
 * @property pickups Unlocks inside the focus stretches.
 * @property longestStretchSeconds The longest stretch inside focus with no unlock (the phone down).
 * @property distractionMinutes Time in the profile's distracting apps inside focus, whole minutes.
 */
data class PhoneSessionUsageSummary(val pickups: Int?, val longestStretchSeconds: Long?, val distractionMinutes: Int?)

/** A stretch of the session that counts as focus. */
data class PhoneFocusSpan(val from: Instant, val to: Instant)

/** Pure rules behind [PhoneSessionUsageSummary]: the app reads the unlocks and the apps' time from Android and hands them in. */
object PhoneSessionUsageRules {
    /** The session from its start to [end], less every stretch away (each kind is out of focus, ADR 77): the stretches pickups and distraction are counted in. */
    fun focusSpans(record: PhoneSessionRecord, end: Instant): List<PhoneFocusSpan> {
        val spans = mutableListOf<PhoneFocusSpan>()
        var from = record.startedAt
        for (away in record.away.sortedBy { it.from }) {
            val awayEnd = minOf(away.to ?: end, end)
            if (away.from.isAfter(from)) spans += PhoneFocusSpan(from, minOf(away.from, end))
            if (awayEnd.isAfter(from)) from = awayEnd
        }
        if (end.isAfter(from)) spans += PhoneFocusSpan(from, end)
        return spans.filter { it.to.isAfter(it.from) }
    }

    /** The unlocks that fall inside a focus stretch. */
    fun pickupsIn(spans: List<PhoneFocusSpan>, unlocks: List<Instant>): List<Instant> =
        unlocks.filter { unlock -> spans.any { !unlock.isBefore(it.from) && unlock.isBefore(it.to) } }.sorted()

    /** The longest stretch inside focus with no unlock in it (a focus stretch with none counts whole). */
    fun longestStretch(spans: List<PhoneFocusSpan>, unlocks: List<Instant>): Duration {
        var longest = Duration.ZERO
        for (span in spans) {
            var last = span.from
            for (unlock in unlocks.filter { !it.isBefore(span.from) && it.isBefore(span.to) }.sorted()) {
                longest = maxOf(longest, Duration.between(last, unlock))
                last = unlock
            }
            longest = maxOf(longest, Duration.between(last, span.to))
        }
        return longest
    }

    /**
     * The summary at [end]. [unlocks] null = not readable (no Usage access): pickups and the stretch are null. [distractionSeconds] null = not readable or the
     * profile names no distracting app: null (shown as a dash), never a zero that was not measured.
     */
    fun summarize(record: PhoneSessionRecord, end: Instant, unlocks: List<Instant>?, distractionSeconds: Long?): PhoneSessionUsageSummary {
        val spans = focusSpans(record, end)
        val inside = unlocks?.let { pickupsIn(spans, it) }
        return PhoneSessionUsageSummary(
            pickups = inside?.size,
            longestStretchSeconds = inside?.let { longestStretch(spans, it).seconds },
            distractionMinutes = distractionSeconds?.let { (it / 60).toInt() },
        )
    }
}
