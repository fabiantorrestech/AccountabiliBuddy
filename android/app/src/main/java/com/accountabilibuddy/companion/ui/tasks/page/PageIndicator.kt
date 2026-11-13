package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.animation.core.animateDpAsState
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfPaging

/**
 * The page indicator under the Shelf's card (boards Tasks-Phone and Tasks-Phone-Label): 6 dp dots with the current one an 18 × 6 dp
 * bar, each a tap that jumps to its page; with more than five pages a count pill instead ("4 / 9"). It always takes the same 26 dp,
 * so the card above never changes height (one page, as Group by None has, leaves it empty).
 */
@Composable
fun PageIndicator(titles: List<String>, index: Int, onJump: (Int) -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val count = titles.size
    Box(modifier.fillMaxWidth().height(26.dp).semantics { contentDescription = "Page ${index + 1} of $count" }, contentAlignment = Alignment.Center) {
        when {
            count <= 1 -> Unit
            ShelfPaging.usesCount(count) -> Text(
                ShelfPaging.countText(index, count),
                style = Ab.type.small.copy(fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold, color = colors.muted,
                modifier = Modifier.border(1.dp, colors.line, CircleShape).padding(horizontal = 10.dp, vertical = 2.dp),
            )
            else -> Row(horizontalArrangement = Arrangement.spacedBy(0.dp), verticalAlignment = Alignment.CenterVertically) {
                titles.forEachIndexed { page, title ->
                    val current = page == index
                    val width by animateDpAsState(if (current) 18.dp else 6.dp, tween(170), label = "dot")
                    // The dot is 6 dp; its touch target is the 26 dp tall box around it.
                    Box(
                        Modifier.height(26.dp).clickable(role = Role.Tab, onClickLabel = "Show $title") { onJump(page) }.padding(horizontal = 3.dp),
                        contentAlignment = Alignment.Center,
                    ) {
                        Box(Modifier.width(width).height(6.dp).background(if (current) colors.ink else colors.faint, CircleShape))
                    }
                }
            }
        }
    }
}
