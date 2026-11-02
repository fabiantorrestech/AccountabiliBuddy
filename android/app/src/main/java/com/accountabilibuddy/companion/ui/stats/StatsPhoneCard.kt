package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.stats.StatsFormat
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbTonalButton
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.companion.usage.PhoneUsage
import java.time.Duration

/**
 * Today's time on the phone by app, the content the old "Phone today" card held (it moved here from Stats' first version): the total, the
 * part during focus, and each app. Read from Android's usage records on this phone, never sent anywhere. Without Usage access it asks.
 */
@Composable
fun StatsPhoneCard(usage: PhoneUsage?, onAllowUsage: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    AbCard(modifier) {
        Text("Phone today", style = Ab.type.section, color = colors.ink)
        if (usage == null) {
            Text("Time on the phone, and during focus.", style = Ab.type.bodyMedium, color = colors.muted)
            AbTonalButton("Allow usage access", onClick = onAllowUsage)
            Text("Needs Usage access. What it reads stays on the phone.", style = Ab.type.small, color = colors.muted)
            return@AbCard
        }

        val total = if (usage.total < Duration.ofMinutes(1)) "Nothing yet today." else "${StatsFormat.hoursMinutes(usage.total)} on the phone"
        Text(total, style = Ab.type.bodyMedium, color = colors.ink)
        if (!usage.inFocus.isZero) Text("${StatsFormat.hoursMinutes(usage.inFocus)} during focus", style = Ab.type.bodyMedium, color = Ab.meaning(colors.drift))
        usage.apps.take(MAX_APPS).forEach { app ->
            Row(Modifier.fillMaxWidth().heightIn(min = 40.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(app.label, style = Ab.type.bodyMedium, color = colors.ink, maxLines = 1, modifier = Modifier.weight(1f), overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis)
                if (app.inFocus > Duration.ZERO) Text("${StatsFormat.hoursMinutes(app.inFocus)} in focus", style = Ab.type.small, color = Ab.meaning(colors.drift), maxLines = 1)
                Text(StatsFormat.hoursMinutes(app.total), style = Ab.type.small, color = colors.muted, maxLines = 1)
            }
        }
    }
}

private const val MAX_APPS = 6
