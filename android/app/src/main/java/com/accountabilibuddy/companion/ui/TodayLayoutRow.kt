package com.accountabilibuddy.companion.ui

import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.today.TodayItem
import com.accountabilibuddy.companion.today.TodayPlacement
import com.accountabilibuddy.companion.ui.kit.AbSegmented
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * One row of Settings › Today (round 2, board Today-Layout-Phone): the drag [handle], the part's icon, title and what it is, and on
 * the right where it sits: Shown (on Today), More (a More today chip that opens a half sheet) or Hidden, offering only the places
 * the part may take ([TodayItem.allowed]). A part that is always shown (a guard or an obligation) reads "Always" instead.
 */
@Composable
fun TodayLayoutRow(part: TodayItem, placement: TodayPlacement, handle: @Composable () -> Unit, onPlace: (TodayPlacement) -> Unit) {
    val colors = Ab.colors
    val hidden = placement == TodayPlacement.Hidden
    Row(
        Modifier.fillMaxWidth().heightIn(min = 58.dp).padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        handle()
        Icon(part.icon(), contentDescription = null, tint = if (hidden) colors.faint else colors.muted, modifier = Modifier.size(22.dp))
        Column(Modifier.weight(1f).padding(start = 8.dp)) {
            Text(part.title, style = Ab.type.bodyMedium, fontWeight = FontWeight.Medium, color = if (hidden) colors.faint else colors.ink)
            Text(part.about, style = Ab.type.small, color = if (hidden) colors.faint else colors.muted, maxLines = 2)
        }
        if (part.alwaysShown && !part.canFold) {
            AlwaysPill(part.title)
        } else {
            val options = PLACES.filter { it in part.allowed }
            AbSegmented(
                options = options.map { label(it) },
                selectedIndex = options.indexOf(placement).takeIf { it >= 0 },
                onSelect = { onPlace(options[it]) },
                modifier = Modifier.width(if (options.size == 3) 176.dp else 124.dp)
                    .semantics { contentDescription = "${part.title}: where it shows" },
            )
        }
    }
}

/** The "Always" pill of a part that can't be moved or hidden. */
@Composable
internal fun AlwaysPill(what: String) {
    Text(
        "Always", style = Ab.type.micro, fontWeight = FontWeight.SemiBold, color = Ab.colors.muted,
        modifier = Modifier.border(1.dp, Ab.colors.line, Ab.shapes.pill).padding(horizontal = 10.dp, vertical = 4.dp)
            .semantics { contentDescription = "$what always shows" },
    )
}

private val PLACES = listOf(TodayPlacement.Above, TodayPlacement.Fold, TodayPlacement.Hidden)

private fun label(placement: TodayPlacement): String = when (placement) {
    TodayPlacement.Above -> "Shown"
    TodayPlacement.Fold -> "More"
    TodayPlacement.Hidden -> "Hidden"
}
