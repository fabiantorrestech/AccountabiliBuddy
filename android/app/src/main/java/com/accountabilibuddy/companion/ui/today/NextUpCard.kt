package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.engine.NextUpLine
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab

/** "Next up" on the phone's Today (board Main): what runs now and the next parts of today's plan, each with its time. */
@Composable
fun NextUpCard(lines: List<NextUpLine>) {
    AbCard(contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 8.dp), verticalSpacing = 0.dp) {
        Row(Modifier.heightIn(min = 32.dp).padding(bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text("Next up", style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), fontWeight = FontWeight.Medium, color = Ab.colors.muted)
        }
        NextUpRows(lines)
    }
}

/** The lines themselves, for the card and for the half sheet of the Next up chip. */
@Composable
fun NextUpRows(lines: List<NextUpLine>, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Column(modifier.fillMaxWidth()) {
        lines.forEach { line ->
            Row(Modifier.fillMaxWidth().heightIn(min = 40.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(
                    line.whenText, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold,
                    color = if (line.isNow) colors.accent else colors.muted, maxLines = 1, modifier = Modifier.widthIn(min = 70.dp),
                )
                Text(line.text, style = Ab.type.bodyMedium, color = colors.ink, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            }
        }
    }
}
