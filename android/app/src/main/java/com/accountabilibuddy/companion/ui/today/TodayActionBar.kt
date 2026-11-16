package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.automirrored.outlined.DirectionsWalk
import androidx.compose.material.icons.outlined.Computer
import androidx.compose.material.icons.outlined.MoreHoriz
import androidx.compose.material.icons.outlined.PhoneAndroid
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.StepAwayState
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The action bar in Today's thumb zone during a session (brief §4.1, CN §3): Done ("Done · b" for a step), Step away / I'm back, and ⋯ for
 * the Session menu. Done finishes the current item ([CurrentItem]); with nothing to finish (planning, a break, every task ticked) it
 * stays in its place, greyed, so the other buttons don't move (CN §1 rule 3).
 */
@Composable
fun SessionActionBar(current: CurrentItem?, canDone: Boolean, onDone: () -> Unit, stepAway: StepAwayState?, onMore: () -> Unit, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
        AbPrimaryButton(
            text = current?.doneLabel ?: "Done",
            onClick = onDone, enabled = current != null && canDone, icon = Icons.Outlined.Check, modifier = Modifier.weight(1.15f),
        )
        AbTonalButton(
            text = if (stepAway?.away == true) "I'm back" else "Step away",
            onClick = { stepAway?.toggle?.invoke() }, enabled = stepAway != null && stepAway.enabled,
            icon = Icons.AutoMirrored.Outlined.DirectionsWalk, modifier = Modifier.weight(1f),
        )
        MoreButton("Session menu", onMore)
    }
}

/** The action bar with nothing running: the one way to start. It opens the Start sheet, where "On <PC>" or "On this phone" is chosen (PS3a, R16). */
@Composable
fun StartActionBar(onStart: () -> Unit, modifier: Modifier = Modifier) {
    AbPrimaryButton("Start", onClick = onStart, icon = Icons.Outlined.PlayArrow, modifier = modifier.fillMaxWidth())
}

/**
 * The bar while another device of the group has a live session (PS3a, R1, R2): "Running on Pixel · 32 min left", in the place of Start so nothing moves (CN §1
 * rule 3). It is not pressable: only that device can end or pause its session (R6). [text] is [com.accountabilibuddy.shared.session.PhoneStartRules.runningOnText].
 */
@Composable
fun RunningElsewhereBar(text: String, isPhone: Boolean, modifier: Modifier = Modifier) {
    Surface(modifier = modifier.fillMaxWidth(), shape = Ab.shapes.pill, color = Ab.colors.surface2, contentColor = Ab.colors.ink) {
        Row(
            Modifier.heightIn(min = 52.dp).padding(horizontal = 16.dp).semantics(mergeDescendants = true) { contentDescription = "$text. Only that device can end it." },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(8.dp, Alignment.CenterHorizontally),
        ) {
            Icon(if (isPhone) Icons.Outlined.PhoneAndroid else Icons.Outlined.Computer, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(20.dp))
            Text(text, style = Ab.type.bodyMedium, color = Ab.colors.ink, maxLines = 1, overflow = TextOverflow.Ellipsis)
        }
    }
}

/** A round 52 dp ⋯ button on the tonal surface, as tall as the buttons beside it; [label] is what a screen reader says. */
@Composable
fun MoreButton(label: String, onClick: () -> Unit, modifier: Modifier = Modifier) {
    Surface(onClick = onClick, modifier = modifier.size(52.dp), shape = CircleShape, color = Ab.colors.surface2, contentColor = Ab.colors.ink) {
        Box(contentAlignment = Alignment.Center) {
            Icon(Icons.Outlined.MoreHoriz, contentDescription = label, modifier = Modifier.size(22.dp))
        }
    }
}
