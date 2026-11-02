package com.accountabilibuddy.companion.ui.stats

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.stats.StatCard
import com.accountabilibuddy.companion.stats.StatsView
import com.accountabilibuddy.companion.ui.theme.Ab

/** The tiles under a chart, two to a row on the second tone (boards 19e to 19g). A tile that names a view opens it. */
@Composable
fun StatTiles(cards: List<StatCard>, modifier: Modifier = Modifier, onOpenView: ((StatsView) -> Unit)? = null) {
    if (cards.isEmpty()) return
    Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(10.dp)) {
        cards.chunked(2).forEach { pair ->
            Row(Modifier.fillMaxWidth().height(IntrinsicSize.Max), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                pair.forEach { card -> StatTile(card, Modifier.weight(1f).fillMaxHeight(), onOpenView) }
                if (pair.size == 1) Spacer(Modifier.weight(1f))
            }
        }
    }
}

@Composable
private fun StatTile(card: StatCard, modifier: Modifier, onOpenView: ((StatsView) -> Unit)?) {
    val colors = Ab.colors
    val body: @Composable () -> Unit = {
        Column(Modifier.padding(horizontal = 14.dp, vertical = 12.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
            Text(card.title, style = Ab.type.small, color = colors.muted)
            // "11 AM–1 PM" may only break after its dash: its words stay together.
            Text(card.value.replace(' ', NO_BREAK_SPACE).replace("–", "–$ZERO_WIDTH_SPACE"), style = Ab.type.timerSmall.copy(fontSize = 24.sp, lineHeight = 28.sp), color = colors.ink, fontWeight = FontWeight.SemiBold)
            Text(card.detail, style = Ab.type.micro, color = colors.muted)
        }
    }
    val view = card.view
    if (view != null && onOpenView != null) {
        Surface(onClick = { onOpenView(view) }, modifier = modifier, shape = Ab.shapes.mediumShape, color = colors.surface2, contentColor = colors.ink, content = body)
    } else {
        Surface(modifier = modifier, shape = Ab.shapes.mediumShape, color = colors.surface2, contentColor = colors.ink, content = body)
    }
}

private const val NO_BREAK_SPACE = '\u00A0'
private const val ZERO_WIDTH_SPACE = "\u200B"
