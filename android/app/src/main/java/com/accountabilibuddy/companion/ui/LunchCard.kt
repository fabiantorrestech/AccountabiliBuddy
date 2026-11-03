package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.lunch.LunchStore
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.shared.WatchLunch
import com.accountabilibuddy.shared.sync.LunchDelivery
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
    var now by remember { mutableStateOf(Instant.now()) }
    LaunchedEffect(taken) {
        while (true) {
            now = Instant.now()
            kotlinx.coroutines.delay(1_000)
        }
    }
    val zone = ZoneId.systemDefault()
    fun time(at: Instant) = clock.time(LocalDateTime.ofInstant(at, zone))
    val onLunch = taken != null && now < taken.end
    val length = LunchStore.formatLength(lunch.lengthMinutes)
    AbCard(verticalSpacing = 12.dp) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Column(Modifier.weight(1f)) {
                val text = when {
                    onLunch -> {
                        val left = Duration.between(now, taken.end).seconds.coerceAtLeast(0)
                        "Lunch · %d:%02d left · until %s".format(left / 60, left % 60, time(taken.end))
                    }
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
                onLunch -> AbTonalButton("I'm back", onClick = lunch.onBack)
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
