package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.ui.kit.AbDialog
import com.accountabilibuddy.companion.ui.kit.AbSectionLabel
import com.accountabilibuddy.companion.ui.theme.Ab
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.util.Locale

/**
 * When turned-off blocks come back on, per kind of day (ADR 37): Workdays and Off-Days, midnight unless changed. Each
 * opens a time wheel; the change is a day plan edit and goes to the PC. One-off days keep their own ("Plan a day").
 */
@Composable
internal fun ReenableTimes(onSet: (Boolean, LocalTime) -> String?) {
    val app = LocalContext.current.applicationContext as CompanionApp
    val plan = app.store.state.collectAsState().value.document.dayPlan
    var editing by remember { mutableStateOf<Boolean?>(null) }
    var error by remember { mutableStateOf<String?>(null) }

    Column {
        AbSectionLabel("Blocks come back on at", Modifier.padding(top = 4.dp))
        listOf(false to "Workdays", true to "Off-Days").forEach { (offDay, name) ->
            val time = (plan.typicalTemplate(workday = !offDay) ?: if (offDay) plan.otherDay else plan.workday).rules.reenableAt
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp)
                    .clickable(enabled = !LocalGuardsLocked.current, role = Role.Button) { editing = offDay },
            ) {
                Text(name, style = Ab.type.body, color = Ab.colors.ink, modifier = Modifier.weight(1f))
                Text(timeText(time), style = Ab.type.body, fontWeight = FontWeight.SemiBold, color = Ab.colors.ink)
            }
        }
        error?.let { Note(it, color = Ab.colors.drift) }
    }

    editing?.let { offDay ->
        var picked by remember(offDay) { mutableStateOf((plan.typicalTemplate(workday = !offDay) ?: if (offDay) plan.otherDay else plan.workday).rules.reenableAt) }
        AbDialog(
            title = "${if (offDay) "Off-Days" else "Workdays"}: blocks back on at",
            onDismiss = { editing = null },
            confirmText = "Save",
            onConfirm = { error = onSet(offDay, picked); editing = null },
        ) {
            Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.fillMaxWidth()) {
                TimeWheel(picked, { picked = it })
                Note("12:00 AM = midnight, the end of the day.", Modifier.padding(top = 8.dp))
            }
        }
    }
}

private val format = DateTimeFormatter.ofPattern("h:mm a", Locale.US)

private fun timeText(time: LocalTime) = if (time == LocalTime.MIDNIGHT) "Midnight" else time.format(format)
