package com.accountabilibuddy.companion.ui.goals

import androidx.compose.ui.text.withStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.goals.GoalTile

/**
 * The goal page's tiles (brief §4.3 Habit board): Today and This period side by side, a full-width Target tile (with "What it takes ›"),
 * and Streak / Total side by side when switched on. The slots keep a minimum height so the page doesn't jump while its numbers load.
 */
@Composable
fun GoalTilesRow(tiles: List<GoalTile>, tint: Color, onWhatItTakes: (() -> Unit)?, modifier: Modifier = Modifier) {
    Column(modifier, verticalArrangement = Arrangement.spacedBy(12.dp)) {
        val pair = tiles.filter { it.kind == "today" || it.kind == "period" }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            pair.forEach { GoalTileCard(it, tint, Modifier.weight(1f)) }
        }
        tiles.firstOrNull { it.kind == "target" }?.let { GoalTileCard(it, tint, Modifier.fillMaxWidth(), onWhatItTakes) }
        val extras = tiles.filter { it.kind == "streak" || it.kind == "total" }
        if (extras.isNotEmpty()) {
            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                extras.forEach { GoalTileCard(it, tint, Modifier.weight(1f)) }
                if (extras.size == 1) Column(Modifier.weight(1f)) {}
            }
        }
    }
}

@Composable
private fun GoalTileCard(tile: GoalTile, tint: Color, modifier: Modifier = Modifier, onWhatItTakes: (() -> Unit)? = null) {
    val colors = Ab.colors
    // Board Goals-Phone-Habit: caption, the number, one line of text, and the bar or dots last. The Target tile's line reads
    // "On track · done ~1/10 · What it takes ›" (the status in its colour, the link in the accent); the whole tile opens it.
    AbCard(modifier.heightIn(min = 96.dp), onClick = onWhatItTakes, contentPadding = androidx.compose.foundation.layout.PaddingValues(14.dp), verticalSpacing = 6.dp) {
        Text(tile.caption, style = Ab.type.small, color = colors.muted, maxLines = 2)
        Row(verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            Text(tile.big, style = Ab.type.bodyMedium.copy(fontSize = 22.sp, fontFeatureSettings = "tnum"), fontWeight = FontWeight.SemiBold, color = colors.ink)
            tile.of?.let { Text(it, style = Ab.type.small.copy(fontFeatureSettings = "tnum"), color = colors.muted, modifier = Modifier.padding(bottom = 2.dp)) }
        }
        if (tile.status != null || tile.foot != null || onWhatItTakes != null) {
            Text(
                buildAnnotatedString {
                    var first = true
                    fun part(block: () -> Unit) {
                        if (!first) append(" · ")
                        first = false
                        block()
                    }
                    tile.status?.let { status ->
                        part { withStyle(SpanStyle(color = if (tile.statusAmber) colors.amberInk else colors.met, fontWeight = FontWeight.Medium)) { append(status) } }
                    }
                    tile.foot?.let { foot -> part { append(foot) } }
                    if (onWhatItTakes != null) part { withStyle(SpanStyle(color = colors.accent, fontWeight = FontWeight.SemiBold)) { append("What it takes ›") } }
                },
                style = Ab.type.small, color = colors.muted,
            )
        }
        tile.fraction?.let { GoalTileBar(it, tile.mark, tint) }
        tile.segments?.takeIf { it.isNotEmpty() }?.let { GoalWeekDots(it, tint) }
    }
}

// A full-width bar; the small tick is where you should be today (Target).
@Composable
private fun GoalTileBar(fraction: Double, mark: Double?, tint: Color) {
    val track = Ab.colors.surface2
    val ink = Ab.colors.ink
    Canvas(Modifier.fillMaxWidth().height(8.dp)) {
        val radius = CornerRadius(size.height / 2, size.height / 2)
        drawRoundRect(track, cornerRadius = radius)
        val share = fraction.coerceIn(0.0, 1.0).toFloat()
        if (share > 0f) drawRoundRect(tint, size = Size(size.width * share, size.height), cornerRadius = radius)
        if (mark != null) {
            val x = size.width * mark.toFloat()
            drawRect(ink, Offset(x - 1.dp.toPx(), 0f), Size(2.dp.toPx(), size.height))
        }
    }
}
