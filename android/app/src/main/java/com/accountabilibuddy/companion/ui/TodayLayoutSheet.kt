package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Checklist
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material.icons.outlined.Timer
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.today.TodayLayout
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Settings › Today on the phone (round 2, brief §10 Q12; board Today-Layout-Phone), mirroring the PC's Settings › Today: two layouts,
 * In a session and Not in a session, each with every part Shown / More (a More today chip) / Hidden and one drag ≡ order. What a
 * session says (its card with the time and This session) is not a part and always shows; the guard banner, the lunch chip and
 * "Link a PC" always show, and "Need it for work", "Not working today" and the import card can't be hidden ([com.accountabilibuddy.companion.today.TodayItem.allowed]).
 *
 * Until the in-session layout is changed here a session uses the other one, as before ([UiPrefs.todaySessionLayout] null). It only
 * changes what Today draws: no guard, wait or lock is touched, nothing is synced, and it is not locked during a session.
 */
@Composable
fun TodayLayoutSheet(onDismiss: () -> Unit, startInSession: Boolean = false) {
    val context = LocalContext.current
    val idle by UiPrefs.todayLayout.collectAsState()
    val ownSession by UiPrefs.todaySessionLayout.collectAsState()
    var inSession by rememberSaveable { mutableStateOf(startInSession) }
    // Until a session has its own layout it follows the other one, with the parts that have a session default at that default.
    val sessionLayout = ownSession ?: idle.copy(session = true)
    val layout = if (inSession) sessionLayout else idle
    val other = (if (inSession) idle else sessionLayout).forState(inSession)
    val companion = com.accountabilibuddy.companion.guidance.isCompanionMode()
    val save: (TodayLayout) -> Unit = { next -> if (inSession) UiPrefs.setTodaySessionLayout(context, next) else UiPrefs.setTodayLayout(context, next) }

    AbSheet(
        title = "Today layout",
        subtitle = "Each state keeps its own layout. Drag ≡ to reorder.",
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
        footer = {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                AbTonalButton("Reset", onClick = { save(TodayLayout(session = inSession)) }, enabled = !layout.isDefault, modifier = Modifier.weight(1f))
                AbPrimaryButton("Copy the other layout", onClick = { save(other) }, enabled = other != layout, modifier = Modifier.weight(1.6f))
            }
        },
    ) {
        Column(Modifier.fillMaxWidth().padding(horizontal = 20.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
            AbSegmented(listOf("In a session", "Not in a session"), selectedIndex = if (inSession) 0 else 1, onSelect = { inSession = it == 0 })
            AbSectionLabel(if (inSession) "Always shown in a session" else "Always shown", Modifier.padding(top = 12.dp))
            if (inSession) {
                FixedRow(Icons.Outlined.Timer, "Time left", "In the session card · Step away and ⋯")
                FixedRow(Icons.Outlined.Checklist, "This session", "Today's task sections, the current one first")
            } else {
                FixedRow(Icons.Outlined.PlayArrow, "Start", "What's next and Start on your PC")
            }
            AbSectionLabel("You choose", Modifier.padding(top = 12.dp))
            ReorderableRows(
                items = layout.orderFor(inSession),
                onReorder = { save(layout.withOrder(it)) },
                label = { it.title },
            ) { part, handle ->
                TodayLayoutRow(part, layout.placementOf(part), handle, onPlace = { save(layout.withPlacement(part, it)) })
            }
            Text(
                if (companion) {
                    "The guard banner, the lunch chip and Link a PC always show. Need it for work, Not working today and the import card " +
                        "can't be hidden. This phone only."
                } else {
                    "Outside PB (ProductaBuddy), the guard banner and lunch chip always show while a guard is on. Link a PC always shows " +
                        "too. Need it for work, Not working today and the import card can't be hidden. This phone only."
                },
                style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(top = 12.dp),
            )
        }
    }
}

// A part of Today that is not in the layout (it always shows): no handle, no choice, the "Always" pill.
@Composable
private fun FixedRow(icon: ImageVector, title: String, about: String) {
    Row(
        Modifier.fillMaxWidth().heightIn(min = 58.dp).padding(start = 48.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        Icon(icon, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(22.dp))
        Column(Modifier.weight(1f).padding(start = 8.dp)) {
            Text(title, style = Ab.type.bodyMedium, fontWeight = FontWeight.Medium, color = Ab.colors.ink)
            Text(about, style = Ab.type.small, color = Ab.colors.muted)
        }
        AlwaysPill(title)
    }
}
