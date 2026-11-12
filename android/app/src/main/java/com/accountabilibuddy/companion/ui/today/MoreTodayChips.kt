package com.accountabilibuddy.companion.ui.today

import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.material3.Icon
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * One chip of More today: what it says, its icon, how many things wait in it ([count], "Goals 3", "Come back to 2"; null or 0 for
 * none) and what a tap opens (a half-height sheet, or the Noise picker).
 */
class MoreTodayChip(val key: String, val label: String, val icon: ImageVector, val count: Int? = null, val onOpen: () -> Unit)

/**
 * "More today" on the phone (brief §4.1, CN §3; boards Main, B-Phone-Session): the parts the layout puts in More today, as a row of
 * 44 dp chips on the surface tone (no border, the theme's small radius, an 18 dp icon, the count after the name) that scrolls
 * sideways past the pane's edge; each opens a sheet over Today, so nothing on the page moves (CN §1 rule 2). Nothing is drawn with
 * no chips.
 *
 * @param edge The pane's side padding: the label and the first chip start there, and the row scrolls under it at the end.
 */
@Composable
fun MoreTodayChipRow(chips: List<MoreTodayChip>, modifier: Modifier = Modifier, edge: Dp = 16.dp) {
    if (chips.isEmpty()) return
    val colors = Ab.colors
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Text("More today", style = Ab.type.micro.copy(fontSize = 12.5.sp, letterSpacing = 0.sp), fontWeight = FontWeight.SemiBold, color = colors.faint, modifier = Modifier.padding(start = edge + 4.dp))
        Row(
            Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = edge),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
        ) {
            chips.forEach { chip ->
                val count = chip.count?.takeIf { it > 0 }
                Surface(
                    onClick = chip.onOpen,
                    shape = Ab.shapes.smallShape,
                    color = colors.surface,
                    contentColor = colors.ink,
                    // 44 dp as drawn; the row's own spacing keeps neighbours apart, and the chip is a button to a screen reader.
                    modifier = Modifier.semantics { role = Role.Button; contentDescription = if (count != null) "${chip.label}, $count" else chip.label },
                ) {
                    Row(
                        Modifier.height(44.dp).padding(start = 12.dp, end = 14.dp),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        Icon(chip.icon, contentDescription = null, tint = colors.muted, modifier = Modifier.size(18.dp))
                        Text(chip.label, style = Ab.type.small.copy(fontSize = 14.sp), fontWeight = FontWeight.Medium, maxLines = 1)
                        if (count != null) {
                            Text(count.toString(), style = Ab.type.micro.copy(fontFeatureSettings = "tnum", letterSpacing = 0.sp), fontWeight = FontWeight.Bold, color = colors.muted)
                        }
                    }
                }
            }
        }
    }
}
