package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ViewStream
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
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.today.TodayLayout
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.kit.AbSegmentedCompact
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * Settings › Today on the phone (round 2, brief §10 Q12; board Today-Layout-Phone), mirroring the PC's Settings › Today: two layouts,
 * In a session and Not in a session, each with every part Shown / More (a More today chip) / Hidden and one drag ≡ order. TD1: every
 * part can be hidden in both layouts, the session card ("Time left and Start") and This session included, except the ones that carry a
 * guard or an obligation: the guard banner, the lunch chip, "Link a PC", "Need it for work", "Not working today" and the import card
 * ([com.accountabilibuddy.companion.today.TodayItem.allowed]). Ending or stepping away from a running session stays in the action bar
 * at the foot of Today, which is not a part. A "More today bar" Show / Hide choice sits above the parts.
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
            // TD1: the More today bar can be hidden whole (this phone, both layouts); nothing else moves.
            val moreTodayBar by UiPrefs.moreTodayBar.collectAsState()
            Row(Modifier.fillMaxWidth().heightIn(min = 58.dp).padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Icon(Icons.Outlined.ViewStream, contentDescription = null, tint = Ab.colors.muted, modifier = Modifier.size(22.dp))
                Column(Modifier.weight(1f)) {
                    Text("More today bar", style = Ab.type.bodyMedium, fontWeight = FontWeight.Medium, color = Ab.colors.ink)
                    Text("The chips for the parts you put in More today", style = Ab.type.small, color = Ab.colors.muted)
                }
                AbSegmentedCompact(
                    options = listOf("Show", "Hide"), selectedIndex = if (moreTodayBar) 0 else 1,
                    onSelect = { UiPrefs.setMoreTodayBar(context, it == 0) },
                    modifier = Modifier.semantics { contentDescription = "More today bar" },
                )
            }
            AbSectionLabel("Parts", Modifier.padding(top = 12.dp))
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
                        "can't be hidden. Step away and End session stay in the bar at the foot of Today. This phone only."
                } else {
                    "Outside PB (ProductaBuddy), the guard banner and lunch chip always show while a guard is on. Link a PC always shows " +
                        "too. Need it for work, Not working today and the import card can't be hidden. Step away and End session stay in the bar at " +
                        "the foot of Today. This phone only."
                },
                style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.padding(top = 12.dp),
            )
        }
    }
}
