package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.automirrored.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.MoreHoriz
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.StepAwayState
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The action bar in Today's thumb zone during a session (brief §4.1, CN §3): Done · step (or · task), Step away / I'm back, and ⋯ for
 * the Session menu. Done finishes the current item ([CurrentItem]); with nothing to finish (planning, a break, every task ticked) it
 * stays in its place, greyed, so the other buttons don't move (CN §1 rule 3).
 */
@Composable
fun SessionActionBar(current: CurrentItem?, canDone: Boolean, onDone: () -> Unit, stepAway: StepAwayState?, onMore: () -> Unit, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        AbPrimaryButton(
            text = when {
                current == null -> "Done"
                current.isStep -> "Done · step"
                else -> "Done · task"
            },
            onClick = onDone, enabled = current != null && canDone, icon = Icons.Outlined.Check, modifier = Modifier.weight(1.15f),
        )
        AbTonalButton(
            text = if (stepAway?.away == true) "I'm back" else "Step away",
            onClick = { stepAway?.toggle?.invoke() }, enabled = stepAway != null && !stepAway.sending,
            icon = Icons.AutoMirrored.Outlined.DirectionsWalk, modifier = Modifier.weight(1f),
        )
        MoreButton("Session menu", onMore)
    }
}

/** The action bar with nothing running: the one way to start, on the controlled PC. */
@Composable
fun StartActionBar(pcName: String, onStart: () -> Unit, modifier: Modifier = Modifier) {
    AbPrimaryButton("Start on $pcName", onClick = onStart, icon = Icons.Outlined.PlayArrow, modifier = modifier.fillMaxWidth())
}

/** A round 48 dp ⋯ button on the tonal surface; [label] is what a screen reader says. */
@Composable
fun MoreButton(label: String, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Surface(onClick = onClick, modifier = modifier.size(48.dp), shape = CircleShape, color = Ab.colors.surface2, contentColor = Ab.colors.ink) {
        Box(contentAlignment = Alignment.Center) {
            Icon(Icons.Outlined.MoreHoriz, contentDescription = label, modifier = Modifier.size(22.dp))
        }
    }
}
