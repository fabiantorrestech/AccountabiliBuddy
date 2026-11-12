package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Eco
import androidx.compose.material.icons.outlined.MoreVert
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The phone's Today header (brief §4.1, CN §3): about 56 dp of space above "Today", then the date with **PB** on one line in
 * ProductaBuddy mode, and a single ⋮ (Today's options) at the end. No gear. The sync light keeps its place beside the ⋮.
 * [line] is the date line ("Wed, Nov 11", or with what the day is).
 */
@Composable
fun TodayTitleHeader(line: String, productaBuddy: Boolean, syncLight: @Composable () -> Unit, onOptions: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Row(modifier.fillMaxWidth().padding(top = 40.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text("Today", style = Ab.type.title, color = colors.ink, modifier = Modifier.semantics { heading() })
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(line, style = Ab.type.small, color = colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false))
                if (productaBuddy) {
                    Text("·", style = Ab.type.small, color = colors.muted)
                    Row(
                        Modifier.semantics(mergeDescendants = true) { contentDescription = "ProductaBuddy mode" },
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(3.dp),
                    ) {
                        Icon(Icons.Outlined.Eco, contentDescription = null, tint = colors.met, modifier = Modifier.size(14.dp))
                        Text("PB", style = Ab.type.small, fontWeight = FontWeight.SemiBold, color = colors.met)
                    }
                }
            }
        }
        syncLight()
        IconButton(onClick = onOptions) {
            Icon(Icons.Outlined.MoreVert, contentDescription = "Today options", tint = colors.ink)
        }
    }
}
