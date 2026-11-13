package com.accountabilibuddy.companion.ui.tasks.page

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ArrowDownward
import androidx.compose.material.icons.outlined.SwapHoriz
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.heading
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.accountabilibuddy.companion.sync.OverLimitStore
import com.accountabilibuddy.companion.ui.kit.AbPrimaryButton
import com.accountabilibuddy.companion.ui.kit.AbTextField
import com.accountabilibuddy.companion.ui.sheets.AbSheet
import com.accountabilibuddy.companion.ui.sheets.AbSheetHeight
import com.accountabilibuddy.companion.ui.theme.Ab
import com.accountabilibuddy.shared.tasks.ShelfSession
import com.accountabilibuddy.shared.tasks.TaskBin
import com.accountabilibuddy.shared.tasks.TaskBins
import com.accountabilibuddy.shared.tasks.TaskLists
import com.accountabilibuddy.shared.tasks.TaskRowText

/**
 * "This session is full" (bundle spec §2; the PC's dialog of the same name): "Swap one out to make room for:" and the incoming
 * task in its own highlighted card (an accent outline, a tint, ⬇, the title at 17 sp, "● Label · 25 min · from Today · Queued"),
 * then "Move one to Queued" with one row per task of This session and a round 40 dp ⇄ button, and a quiet underlined **Add
 * anyway** at the foot, which asks why (at least 10 characters; the reason stays on this phone, as "Keep it to 3" always asked).
 *
 * A swap sends that task to Queued and the newcomer takes its place; the toast says so and Undo restores both. Several incoming
 * tasks ([waiting]) are offered one after the other; closing leaves the rest where they were. Nothing here ends, shortens or
 * unlocks anything.
 */
@Composable
fun SessionFullSheet(waiting: List<String>, lists: TaskLists, controller: ShelfController, onDismiss: () -> Unit) {
    val colors = Ab.colors
    // Still waiting: live, open and not in This session yet (a swap, "Add anyway" or another device may have settled it meanwhile).
    val left = waiting.mapNotNull { id -> lists.tasks.firstOrNull { it.id.equals(id, ignoreCase = true) && it.task.isLive }?.task }.filter { it.bin != TaskBin.SESSION && !it.isDone }
    val incoming = left.firstOrNull()
    if (incoming == null) {
        LaunchedEffect(Unit) { onDismiss() }
        return
    }

    var asking by remember(incoming.id) { mutableStateOf(false) }
    var reason by remember(incoming.id) { mutableStateOf("") }
    var short by remember(incoming.id) { mutableStateOf(false) }
    var note by remember(incoming.id) { mutableStateOf<String?>(null) }
    val label = incoming.labelId?.let { id -> lists.labels.map { it.label }.firstOrNull { it.isLive && it.id.equals(id, ignoreCase = true) } }
    val slots = ShelfSession.slots(lists, incoming.id)
    val done = waiting.size - left.size

    AbSheet(
        title = "This session is full",
        subtitle = "Swap one out to make room for:" + (if (waiting.size > 1) " Each swap makes room for one. ${done + 1} of ${waiting.size}." else ""),
        onDismiss = onDismiss,
        height = AbSheetHeight.Tall,
    ) {
        val card = RoundedCornerShape(18.dp)
        Row(
            Modifier.padding(horizontal = 24.dp, vertical = 6.dp).fillMaxWidth().clip(card).background(colors.accent.copy(alpha = 0.10f))
                .border(1.5.dp, colors.accent.copy(alpha = 0.55f), card).padding(14.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            Icon(Icons.Outlined.ArrowDownward, contentDescription = null, tint = colors.accent, modifier = Modifier.size(22.dp))
            Column(Modifier.weight(1f)) {
                Text(incoming.title, style = Ab.type.body.copy(fontSize = 17.sp), fontWeight = FontWeight.SemiBold, color = colors.ink, maxLines = 3, overflow = TextOverflow.Ellipsis)
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    LabelDot(label?.color, 8.dp)
                    val parts = listOfNotNull(label?.name, incoming.estimateMinutes?.let { TaskRowText.minutes(it) }, "from " + TaskBins.place(incoming.bin))
                    Text(parts.joinToString(" · "), style = Ab.type.small, color = colors.muted, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
            }
        }

        if (!asking) {
            Text(
                "Move one to Queued", style = Ab.type.small.copy(fontSize = 12.5.sp), fontWeight = FontWeight.Bold, color = colors.muted,
                modifier = Modifier.padding(start = 24.dp, end = 24.dp, top = 14.dp, bottom = 4.dp).semantics { heading() },
            )
            if (slots.isEmpty()) {
                Text("No single swap makes room for it. Finish one of this session's actions first, or add it anyway.", style = Ab.type.small, color = colors.muted, modifier = Modifier.padding(horizontal = 24.dp, vertical = 8.dp))
            }
            slots.forEachIndexed { index, task ->
                Row(
                    Modifier.fillMaxWidth().heightIn(min = 58.dp).padding(horizontal = 24.dp, vertical = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text((index + 1).toString(), style = Ab.type.bodyMedium.copy(fontFeatureSettings = "tnum"), color = colors.muted)
                    Text(task.title, style = Ab.type.bodyMedium.copy(fontSize = 15.5.sp), color = colors.ink, maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
                    Box(
                        // 40 dp as drawn, inside a 48 dp touch target.
                        Modifier.size(48.dp).clip(CircleShape).clickable(role = Role.Button) {
                            if (!controller.swap(incoming.id, task.id)) note = "That one no longer makes room. Pick another."
                        }.semantics { contentDescription = "Swap with ${task.title}: it goes to Queued" },
                        contentAlignment = Alignment.Center,
                    ) {
                        Box(Modifier.size(40.dp).background(colors.surface2, CircleShape).border(1.dp, colors.line, CircleShape), contentAlignment = Alignment.Center) {
                            Icon(Icons.Outlined.SwapHoriz, contentDescription = null, tint = colors.ink, modifier = Modifier.size(20.dp))
                        }
                    }
                }
            }
            note?.let { Text(it, style = Ab.type.small, color = colors.amberInk, modifier = Modifier.padding(horizontal = 24.dp, vertical = 4.dp)) }
            Box(Modifier.fillMaxWidth().padding(top = 10.dp), contentAlignment = Alignment.Center) {
                Text(
                    "Add anyway", style = Ab.type.bodyMedium.copy(fontSize = 14.5.sp), color = colors.muted, textDecoration = TextDecoration.Underline,
                    modifier = Modifier.heightIn(min = 48.dp).clip(CircleShape).clickable(role = Role.Button) { asking = true }.padding(horizontal = 18.dp, vertical = 14.dp),
                )
            }
            Text("Closing this leaves it where it is.", style = Ab.type.small, color = colors.faint, modifier = Modifier.padding(horizontal = 24.dp, vertical = 6.dp))
        } else {
            Column(Modifier.padding(horizontal = 24.dp, vertical = 10.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
                Text("Keep it to 3: add it anyway and say why. The reason stays on this phone.", style = Ab.type.bodyMedium, color = colors.ink)
                AbTextField(reason, { reason = it; short = false }, "Why add it anyway", Modifier.fillMaxWidth(), singleLine = false, isError = short)
                if (short) Text("Write why, at least ${OverLimitStore.MIN_REASON_LENGTH} characters.", style = Ab.type.small, color = colors.amberInk)
                Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        "Back", style = Ab.type.bodyMedium, color = colors.muted,
                        modifier = Modifier.heightIn(min = 48.dp).clip(CircleShape).clickable(role = Role.Button) { asking = false }.padding(horizontal = 16.dp, vertical = 14.dp),
                    )
                    AbPrimaryButton("Add anyway", onClick = { if (!controller.addAnyway(incoming.id, reason)) short = true }, modifier = Modifier.weight(1f))
                }
            }
        }
    }
}
