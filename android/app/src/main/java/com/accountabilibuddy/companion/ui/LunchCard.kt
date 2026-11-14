package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.sync.LunchDay
import com.accountabilibuddy.shared.sync.LunchDelivery
import com.accountabilibuddy.shared.sync.LunchOps
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import com.accountabilibuddy.companion.ui.kit.AbQuietIconButton
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * Today's flexible lunch on the phone (More today): on a workday (or a day with a flexible break) "Start lunch" any time for the
 * lunch length, then the time left, +5 / −5 min and "I'm back". The chip outside the fold ([LunchChip]) shows only when it is time.
 */
@Composable
fun LunchCard(lunch: TodayLunch, clock: ClockFormat) {
    val workday = lunch.workday
    val flex = lunch.flex
    val fixed = lunch.fixed
    val taken = lunch.taken
    if (!workday && flex == null && taken == null) return
    // 1 s only while lunch runs (the countdown); otherwise every 30 s, for the fixed lunch window's end.
    val now by rememberTickingNow(taken) { read -> if (taken != null && read < taken.end) 1_000L else 30_000L }
    val zone = ZoneId.systemDefault()
    fun time(at: Instant) = clock.time(LocalDateTime.ofInstant(at, zone))
    val onLunch = taken != null && now < taken.end
    val length = LunchStore.formatLength(lunch.lengthMinutes)
    // ADR 88: after a counted or discarded piece with time left, "Start the rest (N min left)".
    // The rules that act (and the store) run on trusted time; the ticking [now] only makes this redraw (and drives the countdown text).
    val trusted = com.accountabilibuddy.companion.guidance.TrustedTime.now()
    val canStartRest = taken != null && LunchOps.canStartRest(taken, trusted, lunch.lengthMinutes)
    val leftMinutes = if (taken != null) LunchOps.leftMinutes(taken, trusted, lunch.lengthMinutes) else 0
    AbCard(verticalSpacing = 12.dp) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Column(Modifier.weight(1f)) {
                val text = when {
                    onLunch -> {
                        val left = Duration.between(now, taken.end).seconds.coerceAtLeast(0)
                        "Lunch · %d:%02d left · until %s".format(left / 60, left % 60, time(taken.end))
                    }
                    taken != null && taken.discarded -> "Lunch discarded · $leftMinutes min left for today"
                    taken != null && canStartRest ->
                        "Lunch · ${Math.floorDiv(LunchDay.used(taken, now).seconds, 60L)} min counted · $leftMinutes min left for later"
                    taken != null && !taken.counted.isNullOrEmpty() -> "Lunch done · ${Math.floorDiv(LunchDay.used(taken, now).seconds, 60L)} min in all"
                    taken != null -> "Lunch done · ${time(taken.start)} – ${time(taken.end)}"
                    // A fixed lunch period: the PC starts it by itself (this phone doesn't ask about it).
                    // Only until its window ends: after that it can't start by itself any more.
                    fixed != null && LocalDateTime.ofInstant(now, zone).isBefore(fixed.window.end) ->
                        "Lunch starts at ${clock.time(fixed.window.start)} by itself · until ${clock.time(fixed.window.end)}"
                    flex != null -> "${flex.name} · $length · usually ${clock.time(flex.window.start)} – ${clock.time(flex.window.end)}"
                    else -> "Lunch · $length, any time"
                }
                IconTitle(text, style = com.accountabilibuddy.companion.ui.theme.Ab.type.bodyMedium, icon = SectionIcons.of("Lunch"))
            }
            when {
                onLunch -> {
                    AbTonalButton("I'm back", onClick = lunch.onBack)
                    // The ✕ asks Count it / Discard it / Keep going (ADR 88); "I'm back" is Count it without asking.
                    AbQuietIconButton(Icons.Outlined.Close, "End lunch", onClick = lunch.onAsk)
                }
                canStartRest -> AbTonalButton("Start the rest ($leftMinutes min left)", onClick = lunch.onStartRest)
                taken == null -> AbTonalButton("Start lunch", onClick = lunch.onStart)
            }
        }
        // While lunch runs: whether the other devices have it, from their latest files (this card's own refresh, no loop of its own).
        if (onLunch) {
            LunchDelivery.line(taken, lunch.peers, now) { clock.time(LocalDateTime.ofInstant(it, zone)) }?.let {
                Text(it, style = com.accountabilibuddy.companion.ui.theme.Ab.type.small, color = com.accountabilibuddy.companion.ui.theme.Ab.colors.muted)
            }
        }
        if (onLunch) {
            val canShorten = taken.end.isAfter(now.plus(WatchLunch.SHORTEST_LEFT))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                AbTonalButton("−5 min", onClick = { lunch.onAdjust(false) }, enabled = canShorten)
                AbTonalButton("+5 min", onClick = { lunch.onAdjust(true) })
            }
        }
    }
}
