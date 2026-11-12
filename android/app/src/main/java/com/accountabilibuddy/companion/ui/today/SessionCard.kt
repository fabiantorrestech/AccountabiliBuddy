package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.engine.ClockFormat
import com.accountabilibuddy.companion.sync.PcState
import com.accountabilibuddy.companion.ui.BigTimer
import com.accountabilibuddy.companion.ui.clockText
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbRing
import com.accountabilibuddy.companion.ui.rememberTickingNow
import com.accountabilibuddy.companion.ui.secondsLeft
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.Duration
import java.time.Instant
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * The session card at the top of the phone's Today (brief §4.1, board B-Phone-Session): a ring with what is left of the phase, the
 * large time ("32:14", tabular numbers) and "left · ends 1:23 PM". The current task is not repeated here: it is the first row of
 * This session (CN §1 rule 6). Only this card redraws each second. While stepped away it holds at the moment of stepping away, as
 * the hero it replaces did ([com.accountabilibuddy.companion.ui.SessionHero]); [awayText] says so under the time.
 */
@Composable
fun SessionCard(state: PcState, clock: ClockFormat, awayText: String?, awaySince: Instant?) {
    val ticking by rememberTickingNow()
    val now = awaySince?.takeIf { it.isBefore(ticking) } ?: ticking
    val start = state.phaseStartedAt?.toInstant()
    val end = state.phaseEndsAt?.toInstant()
    val seconds = when {
        end != null -> secondsLeft(now, end)
        start != null -> Duration.between(start, now).seconds.coerceAtLeast(0)
        else -> 0L
    }
    // The ring empties as the phase runs out; a phase with no set end shows a full ring.
    val progress = if (start != null && end != null) {
        val span = Duration.between(start, end).seconds.coerceAtLeast(1)
        (seconds.toFloat() / span).coerceIn(0f, 1f)
    } else {
        1f
    }
    val endsText = end?.let { clock.time(LocalDateTime.ofInstant(it, ZoneId.systemDefault())) }
    val caption = when {
        state.pcOff -> "PC off · paused"
        awaySince != null -> "away · paused"
        end != null && state.isBreak -> "break left · ends $endsText"
        end != null -> "left · ends $endsText"
        else -> "so far · no set end"
    }
    val spoken = if (end != null) "${TimeLeftText.compact(seconds)} left" else "${clockText(seconds)} so far"
    AbCard(contentPadding = PaddingValues(horizontal = 18.dp, vertical = 16.dp)) {
        Row(
            Modifier.semantics(mergeDescendants = true) { contentDescription = "$spoken, $caption" },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(16.dp),
        ) {
            Box(contentAlignment = Alignment.Center) {
                AbRing(progress, diameter = 72.dp, strokeWidth = 6.dp)
                Icon(Icons.Outlined.Timer, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(20.dp))
            }
            Column(Modifier.weight(1f)) {
                BigTimer(clockText(seconds), color = if (state.isBreak) Ab.meaning(Ab.colors.sleep) else Ab.colors.ink)
                Text(caption, style = Ab.type.bodyMedium, color = Ab.colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        if (awayText != null) Text(awayText, style = Ab.type.small, color = Ab.colors.muted)
    }
}
