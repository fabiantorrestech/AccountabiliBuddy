package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.animation.core.Animatable
import androidx.compose.animation.core.LinearEasing
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Close
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.graphics.graphicsLayer
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.ui.theme.Ab

/**
 * The Tasks page's toast (board Tasks-Phone-Moved; bundle spec §14): the target's icon, the message with its second line, **Undo**
 * (when it can be taken back), × and a 3 dp timer bar in the accent that runs down, evenly, over the toast's life. Tapping the
 * words goes where the tasks went ([onGo]; only when the toast leads somewhere). When the bar is empty the toast goes ([onGone]).
 *
 * It is announced politely to a screen reader; Undo, × and the tap each have a name. With motion off it appears without the fade.
 */
@Composable
fun TasksToastCard(toast: TasksToast, motionScale: Float, onGo: (() -> Unit)?, onUndo: (() -> Unit)?, onGone: () -> Unit, modifier: Modifier = Modifier) {
    val colors = Ab.colors
    val shape = RoundedCornerShape(18.dp)
    val left = remember(toast) { Animatable(1f) }
    val shown = remember(toast) { Animatable(if (motionScale <= 0f) 1f else 0f) }
    LaunchedEffect(toast) {
        shown.animateTo(1f, tween((150 * motionScale).toInt().coerceAtLeast(1)))
    }
    LaunchedEffect(toast) {
        left.animateTo(0f, tween(toast.length.millis.toInt(), easing = LinearEasing))
        onGone()
    }
    Column(
        modifier.fillMaxWidth().graphicsLayer { alpha = shown.value }.shadow(12.dp, shape).clip(shape).background(colors.surface2)
            .semantics { liveRegion = LiveRegionMode.Polite },
    ) {
        Row(Modifier.fillMaxWidth().heightIn(min = 52.dp).padding(end = 4.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
            val go = if (onGo != null) Modifier.clickable(role = Role.Button, onClickLabel = toast.goLabel ?: "Show", onClick = onGo) else Modifier
            Row(
                Modifier.weight(1f).heightIn(min = 52.dp).then(go).padding(start = 16.dp, top = 12.dp, end = 4.dp, bottom = 12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                toast.icon?.let { Icon(it, contentDescription = null, tint = colors.accent, modifier = Modifier.size(20.dp)) }
                Column(Modifier.weight(1f)) {
                    Text(toast.message, style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 3, overflow = TextOverflow.Ellipsis)
                    toast.detail?.let { Text(it, style = Ab.type.small.copy(fontSize = 12.5.sp), color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis) }
                }
            }
            if (onUndo != null) {
                Box(
                    Modifier.heightIn(min = 48.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onUndo).padding(horizontal = 12.dp),
                    contentAlignment = Alignment.Center,
                ) {
                    Text("Undo", style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), fontWeight = FontWeight.Bold, color = colors.accent)
                }
            }
            Box(
                Modifier.size(44.dp).clip(CircleShape).clickable(role = Role.Button, onClick = onGone).semantics { contentDescription = "Close this message" },
                contentAlignment = Alignment.Center,
            ) {
                Icon(Icons.Outlined.Close, contentDescription = null, tint = colors.muted, modifier = Modifier.size(20.dp))
            }
        }
        Box(Modifier.fillMaxWidth().height(3.dp).background(colors.bg)) {
            Box(Modifier.fillMaxHeight().fillMaxWidth(left.value.coerceIn(0f, 1f)).background(colors.accent))
        }
    }
}
