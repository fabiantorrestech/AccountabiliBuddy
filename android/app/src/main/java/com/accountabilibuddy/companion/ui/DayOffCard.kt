package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier

import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.CompanionApp
import com.accountabilibuddy.companion.nudge.NudgeStore
import java.time.LocalDate

/**
 * On Today after "Not working today" on the work card: says so, with Undo (it only makes the phone stricter again,
 * so no wait).
 */
@Composable
internal fun DayOffCard() {
    val context = LocalContext.current
    var dayOff by remember { mutableStateOf(NudgeStore(context).load().dayOff == LocalDate.now()) }
    if (!dayOff) {
        return
    }

    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            IconTitle("Not working today: no work nudges on this phone.", style = Ab.type.bodyMedium, icon = SectionIcons.of("Not working today"), modifier = Modifier.weight(1f))
            TextButton(onClick = {
                NudgeStore(context).update { it.copy(dayOff = null, dayOffAskedAt = null) }
                dayOff = false
                (context.applicationContext as CompanionApp).refreshSoon()
            }) { Text("Undo", color = Ab.colors.ink) }
        }
    }
}
