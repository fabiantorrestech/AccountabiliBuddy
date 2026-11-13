package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Eco
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * What Tasks shows in place of the Shelf while a session runs (board Tasks-Phone-Session, change note §2): a leaf, "The Shelf is
 * hidden while you focus", how many tasks it holds, the "Show the Shelf" button and where it is set. The strip, + and search stay
 * above it, so the tab is never empty. Shown, the Shelf is at full strength and hides again when Tasks is left.
 */
@Composable
fun ShelfHiddenCard(taskCount: Int, onShow: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    Column(
        modifier.fillMaxWidth().padding(horizontal = 16.dp).background(colors.surface, RoundedCornerShape(28.dp)).padding(start = 20.dp, end = 20.dp, top = 22.dp, bottom = 20.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Icon(Icons.Outlined.Eco, contentDescription = null, tint = colors.muted, modifier = Modifier.size(24.dp))
        Text("The Shelf is hidden while you focus", style = Ab.type.body.copy(fontSize = 16.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, textAlign = TextAlign.Center)
        Text(
            "$taskCount ${if (taskCount == 1) "task" else "tasks"} in To sort, Next and Later. Search and + still work.",
            style = Ab.type.small.copy(fontSize = 13.5.sp, lineHeight = 19.5.sp), color = colors.muted, textAlign = TextAlign.Center,
        )
        Surface(onClick = onShow, shape = CircleShape, color = colors.surface2, contentColor = colors.ink, modifier = Modifier.padding(top = 6.dp)) {
            Text(
                "Show the Shelf", style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.SemiBold,
                modifier = Modifier.heightIn(min = 48.dp).padding(horizontal = 18.dp).padding(vertical = 13.dp),
            )
        }
        Text(
            "Hides again when you leave Tasks · Settings › Tasks › During a session",
            style = Ab.type.micro.copy(fontSize = 12.sp, letterSpacing = 0.sp), color = colors.faint, textAlign = TextAlign.Center, modifier = Modifier.padding(top = 2.dp),
        )
    }
}
