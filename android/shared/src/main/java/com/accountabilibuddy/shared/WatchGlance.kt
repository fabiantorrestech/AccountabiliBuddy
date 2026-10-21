package com.accountabilibuddy.shared

import java.time.Duration
import java.time.Instant
import java.time.ZoneId

/**
 * What the Tile and the watch-face complication show: a ring filled to [value] of [max] (never 0 of 0), a short text
 * for small complications, and a title, headline and detail for the Tile.
 */
data class Glance(val value: Float, val max: Float, val shortText: String, val title: String, val headline: String, val detail: String)

/** Builds the [Glance] (ADR 21): the running session's progress while one runs, else today's focus against the plan. */
object WatchGlance {
    private const val NONE = "—"

    fun from(snapshot: WatchSnapshot?, now: Instant, zone: ZoneId): Glance {
        if (snapshot == null) {
            return Glance(0f, 1f, NONE, "AccountabiliBuddy", "Not linked", "Link it in the phone app")
        }

        if (snapshot.date != now.atZone(zone).toLocalDate()) {
            return Glance(0f, 1f, NONE, "Today", "No data for today", WatchText.updatedAgo(snapshot.createdAt, now) ?: "")
        }

        val focused = snapshot.focusedMinutes(now, zone)
        val running = snapshot.running?.takeIf { it.endsAt == null || it.endsAt.isAfter(now) }
        if (running != null) {
            val title = if (running.onBreak) "Break" else running.profile ?: "Focus"
            val detail = "${WatchText.duration(focused)} focused today"
            val endsAt = running.endsAt
            if (endsAt == null) {
                val elapsed = Duration.between(running.startedAt, now).toMinutes().toInt().coerceAtLeast(0)
                return Glance((elapsed % 60).toFloat(), 60f, WatchText.duration(elapsed), title, "${WatchText.duration(elapsed)} so far", detail)
            }

            val total = Duration.between(running.startedAt, endsAt).toMinutes().toInt().coerceAtLeast(1)
            val left = Duration.between(now, endsAt).toMinutes().toInt().coerceAtLeast(1)
            return Glance((total - left).coerceIn(0, total).toFloat(), total.toFloat(), WatchText.duration(left), title, "${WatchText.duration(left)} left", detail)
        }

        val planned = snapshot.plannedMinutes
        val max = if (planned > 0) planned.toFloat() else 1f
        val value = if (planned > 0) focused.coerceAtMost(planned).toFloat() else 0f
        val headline = if (focused == 0) "No focus yet" else WatchText.duration(focused)
        val detail = if (planned > 0) "of ${WatchText.duration(planned)} planned" else "Nothing planned today"
        return Glance(value, max, WatchText.duration(focused), "Today", headline, detail)
    }
}
