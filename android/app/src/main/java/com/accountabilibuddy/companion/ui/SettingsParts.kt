package com.accountabilibuddy.companion.ui

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.ExpandLess
import androidx.compose.material.icons.outlined.ExpandMore
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.accountabilibuddy.companion.ui.kit.AbCard
import com.accountabilibuddy.companion.ui.kit.AbSwitch
import com.accountabilibuddy.companion.ui.theme.Ab

// The pieces the Settings pages share, on the kit and the theme's tokens: a card that may fold, a switch row, a folded
// explanation and a line of small text. Each page's own file is in ui/settings.

/**
 * A titled card. With a [summary] it folds: the title and the one-line summary show, the rest opens on a tap
 * ([startOpen] for the first look). Without one it's always open.
 */
@Composable
internal fun Section(title: String, summary: String? = null, startOpen: Boolean = false, content: @Composable () -> Unit) {
    var open by rememberSaveable(title) { mutableStateOf(summary == null || startOpen) }
    AbCard(contentPadding = PaddingValues(horizontal = 16.dp, vertical = 12.dp), verticalSpacing = 8.dp) {
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.fillMaxWidth().heightIn(min = 44.dp).let {
                if (summary != null) it.clickable(role = Role.Button, onClickLabel = if (open) "Fold" else "Open") { open = !open } else it
            },
        ) {
            Column(Modifier.weight(1f)) {
                IconTitle(title, style = Ab.type.body, fontWeight = FontWeight.SemiBold)
                if (summary != null && !open) {
                    Text(summary, style = Ab.type.small, color = Ab.colors.muted, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            if (summary != null) {
                Icon(
                    if (open) Icons.Outlined.ExpandLess else Icons.Outlined.ExpandMore,
                    contentDescription = if (open) "Fold" else "Open",
                    tint = Ab.colors.faint,
                    modifier = Modifier.size(28.dp),
                )
            }
        }
        // Folds open and shut smoothly (Settings > Appearance > Animations; at once when off).
        val scale = LocalMotionScale.current
        AnimatedVisibility(
            visible = open,
            enter = expandVertically(motionSpec(scale, 220)) + fadeIn(motionSpec(scale, 220)),
            exit = shrinkVertically(motionSpec(scale, 180)) + fadeOut(motionSpec(scale, 180)),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) { content() }
        }
    }
}

/** A switch with its title; a short [detail] shows under it, a long one behind ⓘ. The whole row toggles. */
@Composable
internal fun ToggleRow(title: String, detail: String, checked: Boolean, enabled: Boolean = true, onChange: (Boolean) -> Unit) {
    var explained by remember { mutableStateOf(false) }
    val long = detail.length > SHORT_TEXT
    // Inside a guard section while a focus session runs (the guard lock) a switch shows its value and takes no tap.
    val active = enabled && !LocalGuardsLocked.current
    Row(
        Modifier.fillMaxWidth().heightIn(min = 56.dp).toggleable(value = checked, enabled = active, role = Role.Switch, onValueChange = onChange),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(title, style = Ab.type.body, fontWeight = FontWeight.Medium, color = if (active) Ab.colors.ink else Ab.colors.faint)
                if (long) {
                    // A 48 x 48 dp target (the glyph is narrow), read as "More about <title>" rather than the glyph's name.
                    Box(
                        Modifier.heightIn(min = 48.dp).widthIn(min = 48.dp).clickable(role = Role.Button) { explained = !explained }
                            .semantics { contentDescription = "More about $title" },
                        contentAlignment = Alignment.Center,
                    ) {
                        Text("ⓘ", style = Ab.type.body, color = Ab.colors.muted, textAlign = TextAlign.Center, modifier = Modifier.clearAndSetSemantics { })
                    }
                }
            }
            if (!long || explained) {
                Text(detail, style = Ab.type.small, color = Ab.colors.muted)
            }
        }
        AbSwitch(checked = checked, enabled = active)
    }
}

/** An explanation folded behind "ⓘ [label]"; a tap shows the [text]. */
@Composable
internal fun Hint(text: String, label: String = "How this works") {
    var open by remember { mutableStateOf(false) }
    Column {
        // Quiet (never the accent) and a 48 dp target.
        Row(Modifier.heightIn(min = 48.dp).clickable(role = Role.Button) { open = !open }, verticalAlignment = Alignment.CenterVertically) {
            Text("ⓘ $label", style = Ab.type.small, color = Ab.colors.muted, modifier = Modifier.semantics { contentDescription = label })
        }
        if (open) {
            Text(text, style = Ab.type.small, color = Ab.colors.muted)
        }
    }
}

/** A line of small muted text under a control or a title. [color] for a warning (the waiting or drift tone). */
@Composable
internal fun Note(text: String, modifier: Modifier = Modifier, color: Color = Ab.colors.muted) {
    Text(text, modifier = modifier, style = Ab.type.small, color = color)
}

/** Longer than this, a switch's detail goes behind ⓘ. */
private const val SHORT_TEXT = 48
